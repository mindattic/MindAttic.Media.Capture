#include "MindAtticCapture.h"

#include "FrameFeed.h"
#include "MindAtticCaptureAgentLocator.h"
#include "MindAtticCaptureSettings.h"
#include "MindAtticMediaCaptureModule.h"

#include "Dom/JsonObject.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "GenericPlatform/GenericWindow.h"
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformTime.h"
#include "Misc/App.h"
#include "Misc/CommandLine.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Widgets/SWindow.h"

// Capture code is compiled into every configuration (Shipping too) and stays off unless -capture is on the command
// line. MINDATTIC_CAPTURE=0 builds a game with none of it.
#ifndef MINDATTIC_CAPTURE
#define MINDATTIC_CAPTURE 1
#endif

namespace MindAttic::Media::Capture
{
	namespace
	{
		/// How long the host gets to answer before the game gives up on it (CaptureRunner's own wait is 15 s).
		constexpr double ReadyTimeoutSeconds = 20.0;

		struct State
		{
			bool Tried = false, IsReady = false, IsAny = false, IsEnabled = false, ShutDown = false;
			FString Folder;
			FProcHandle Proc;
			void* StdinRead = nullptr;   // the host's end
			void* StdinWrite = nullptr;  // ours
			void* StdoutRead = nullptr;  // ours
			void* StdoutWrite = nullptr; // the host's end
			TArray<uint8> Pending;
			int32 NextId = 0;
			struct FResult
			{
				bool Ok = false;
				TArray<FString> Files;
				FString Error;
			};
			TMap<int32, FResult> Results;
			double StartedAt = 0;
		};

		State& S()
		{
			static State state;
			return state;
		}

		FString JsonString(const FString& text)
		{
			FString out;
			out.Reserve(text.Len() + 2);
			out += TEXT('"');
			for (TCHAR c : text)
			{
				switch (c)
				{
				case TEXT('"'): out += TEXT("\\\""); break;
				case TEXT('\\'): out += TEXT("\\\\"); break;
				case TEXT('\n'): out += TEXT("\\n"); break;
				case TEXT('\r'): out += TEXT("\\r"); break;
				case TEXT('\t'): out += TEXT("\\t"); break;
				default:
					if (c < 0x20) out += FString::Printf(TEXT("\\u%04x"), static_cast<int32>(c));
					else out += c;
				}
			}
			out += TEXT('"');
			return out;
		}

		/// One argument for CommandLineToArgvW: quoted, with its quotes and the backslashes before them escaped.
		FString QuoteArg(const FString& arg)
		{
			FString out = TEXT("\"");
			int32 slashes = 0;
			for (TCHAR c : arg)
			{
				if (c == TEXT('\\')) { ++slashes; continue; }
				if (c == TEXT('"'))
				{
					for (int32 i = 0; i < slashes * 2 + 1; ++i) out += TEXT('\\');
					out += TEXT('"');
				}
				else
				{
					for (int32 i = 0; i < slashes; ++i) out += TEXT('\\');
					out += c;
				}
				slashes = 0;
			}
			for (int32 i = 0; i < slashes * 2; ++i) out += TEXT('\\');
			out += TEXT('"');
			return out;
		}

		void* GameWindowHandle()
		{
			if (!GEngine || !GEngine->GameViewport) return nullptr;
			TSharedPtr<SWindow> window = GEngine->GameViewport->GetWindow();
			if (!window.IsValid() || !window->GetNativeWindow().IsValid()) return nullptr;
			return window->GetNativeWindow()->GetOSWindowHandle();
		}

		void Send(const FString& json)
		{
			State& s = S();
			if (!s.StdinWrite || s.ShutDown) return;
			FPlatformProcess::WritePipe(s.StdinWrite, json); // one line: WritePipe adds the newline
		}

		void OnLine(const FString& line)
		{
			State& s = S();
			TSharedPtr<FJsonObject> msg;
			if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(line), msg) || !msg.IsValid()) return;
			FString op;
			msg->TryGetStringField(TEXT("op"), op);
			if (op == TEXT("log"))
			{
				// CaptureUnity's UnityCaptureLog: the engine's own lines in the game's log.
				FString level, text;
				msg->TryGetStringField(TEXT("level"), level);
				msg->TryGetStringField(TEXT("message"), text);
				if (level == TEXT("Error")) { UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] %s"), *text); }
				else if (level == TEXT("Warning")) { UE_LOG(LogMindAtticCapture, Warning, TEXT("[MindAttic.Media.Capture] %s"), *text); }
				else { UE_LOG(LogMindAtticCapture, Display, TEXT("[MindAttic.Media.Capture] %s"), *text); }
			}
			else if (op == TEXT("ready"))
			{
				s.IsReady = true;
				msg->TryGetBoolField(TEXT("any"), s.IsAny);
				msg->TryGetBoolField(TEXT("enabled"), s.IsEnabled);
				msg->TryGetStringField(TEXT("folder"), s.Folder);
				// Recording the game's own frames: read them back at the host's capture rate.
				int32 fps = 0;
				if (s.IsEnabled && msg->TryGetNumberField(TEXT("fps"), fps)) FrameFeed::Start(fps);
				else FrameFeed::Close();
				FString error;
				if (msg->TryGetStringField(TEXT("error"), error) && !error.IsEmpty())
					UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] Capture disabled: %s"), *error);
			}
			else if (op == TEXT("result"))
			{
				int32 id = 0;
				msg->TryGetNumberField(TEXT("id"), id);
				State::FResult result;
				msg->TryGetBoolField(TEXT("ok"), result.Ok);
				msg->TryGetStringField(TEXT("error"), result.Error);
				const TArray<TSharedPtr<FJsonValue>>* files = nullptr;
				if (msg->TryGetArrayField(TEXT("files"), files) && files)
					for (const TSharedPtr<FJsonValue>& f : *files) result.Files.Add(f->AsString());
				if (id != 0) s.Results.Add(id, MoveTemp(result));
				else if (!result.Ok) UE_LOG(LogMindAtticCapture, Warning, TEXT("[MindAttic.Media.Capture] The capture host rejected a request: %s"), *result.Error);
			}
			else if (op == TEXT("stopped"))
			{
				s.IsEnabled = false;
			}
		}

		void Pump()
		{
			State& s = S();
			if (!s.StdoutRead) return;
			TArray<uint8> chunk;
			while (FPlatformProcess::ReadPipeToArray(s.StdoutRead, chunk) && chunk.Num() > 0)
			{
				s.Pending.Append(chunk);
				chunk.Reset();
			}
			int32 start = 0;
			for (int32 i = 0; i < s.Pending.Num(); ++i)
			{
				if (s.Pending[i] != '\n') continue;
				int32 len = i - start;
				if (len > 0 && s.Pending[i - 1] == '\r') --len;
				if (len > 0)
				{
					FUTF8ToTCHAR conv(reinterpret_cast<const ANSICHAR*>(s.Pending.GetData() + start), len);
					OnLine(FString(conv.Length(), conv.Get()));
				}
				start = i + 1;
			}
			if (start > 0) s.Pending.RemoveAt(0, start);
			// The host gave up or died before answering: capture is off.
			if (!s.IsReady && s.Proc.IsValid() && !FPlatformProcess::IsProcRunning(s.Proc))
			{
				s.IsReady = true;
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] The capture host exited before starting; capture is off."));
			}
			if (!s.IsReady && FPlatformTime::Seconds() - s.StartedAt > ReadyTimeoutSeconds)
			{
				s.IsReady = true;
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] The capture host did not answer in %.0f s; capture is off."), ReadyTimeoutSeconds);
			}
		}

		/// `source`: what the host records, "--hwnd 0x..." for the game window or "--frames {GUID}" for the frame feed.
		void Start(const FString& source)
		{
			State& s = S();
			s.Tried = true;
			s.StartedAt = FPlatformTime::Seconds();
			const FString host = AgentLocator::Find();
			if (host.IsEmpty())
			{
				s.IsReady = true;
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] -capture asked for, but no capture agent was found (MINDATTIC_CAPTURE_HOST, MINDATTIC_CAPTURE_AGENT, Capture\\capture.exe beside the game, %%LOCALAPPDATA%%\\MindAttic\\Capture\\bin, PATH); capture is off. MindAttic.Media.Capture's tools/install.ps1 installs it."));
				return;
			}
			if (!FPlatformProcess::CreatePipe(s.StdoutRead, s.StdoutWrite) || !FPlatformProcess::CreatePipe(s.StdinRead, s.StdinWrite, /*bWritePipeLocal*/ true))
			{
				s.IsReady = true;
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] Could not open the pipes to the capture host; capture is off."));
				return;
			}
			const UMindAtticCaptureSettings* settings = GetDefault<UMindAtticCaptureSettings>();
			FString args = FString::Printf(TEXT("host %s --pid %u --app %s --version %s"),
				*source, FPlatformProcess::GetCurrentProcessId(), *QuoteArg(settings->ResolveAppName()), *QuoteArg(FApp::GetBuildVersion()));
			const FString defaults = settings->ResolveDefaultsFile();
			if (!defaults.IsEmpty()) args += FString::Printf(TEXT(" --defaults %s"), *QuoteArg(defaults));
			args += FString::Printf(TEXT(" --cmdline %s"), *QuoteArg(FCommandLine::GetOriginal()));
			uint32 pid = 0;
			s.Proc = FPlatformProcess::CreateProc(*host, *args, /*bLaunchDetached*/ false, /*bLaunchHidden*/ true, /*bLaunchReallyHidden*/ true,
				&pid, 0, *FPaths::GetPath(host), s.StdoutWrite, s.StdinRead);
			// The host's ends are its own now (inherited); ours stay open until Shutdown.
			FPlatformProcess::ClosePipe(s.StdinRead, s.StdoutWrite);
			s.StdinRead = s.StdoutWrite = nullptr;
			if (!s.Proc.IsValid())
			{
				s.IsReady = true;
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] Could not start %s; capture is off."), *host);
				return;
			}
			UE_LOG(LogMindAtticCapture, Display, TEXT("[MindAttic.Media.Capture] Capture host %s started (pid %u)."), *host, pid);
		}

		/// Sends `{"op":op,"id":N<fields>}` and returns N, or 0 when capture is off. `fields` starts with a comma.
		int32 Request(const TCHAR* op, const FString& fields = FString())
		{
			if (!Enabled()) return 0;
			const int32 id = ++S().NextId;
			Send(FString::Printf(TEXT("{\"op\":\"%s\",\"id\":%d%s}"), op, id, *fields));
			return id;
		}

		/// Runs the located agent with `args` (a `capture set` command) and waits for it: its stdout, or false.
		bool RunAgent(const FString& args, FString& out)
		{
			const FString agent = AgentLocator::Find();
			if (agent.IsEmpty())
			{
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] No capture agent was found for `capture %s`. MindAttic.Media.Capture's tools/install.ps1 installs it."), *args);
				return false;
			}
			int32 code = -1;
			FString err;
			if (!FPlatformProcess::ExecProcess(*agent, *args, &code, &out, &err, *FPaths::GetPath(agent)) || code != 0)
			{
				UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] `capture %s` failed (%d): %s"), *args, code, *err.TrimStartAndEnd());
				return false;
			}
			return true;
		}
	}

	Data& Data::Num(const TCHAR* key, double value)
	{
		Fields.Add({ key, FMath::IsFinite(value) ? FString::SanitizeFloat(value, 0) : FString(TEXT("null")) });
		return *this;
	}

	Data& Data::Str(const TCHAR* key, const FString& value)
	{
		Fields.Add({ key, JsonString(value) });
		return *this;
	}

	Data& Data::Bool(const TCHAR* key, bool value)
	{
		Fields.Add({ key, value ? FString(TEXT("true")) : FString(TEXT("false")) });
		return *this;
	}

	FString Data::ToJson() const
	{
		FString out = TEXT("{");
		for (int32 i = 0; i < Fields.Num(); ++i)
		{
			if (i > 0) out += TEXT(",");
			out += JsonString(Fields[i].Key) + TEXT(":") + Fields[i].Value;
		}
		return out + TEXT("}");
	}

	bool Requested()
	{
#if MINDATTIC_CAPTURE
		static const bool requested = []
		{
			TArray<FString> tokens, switches;
			FCommandLine::Parse(FCommandLine::Get(), tokens, switches);
			for (const FString& t : tokens)
			{
				FString a = t;
				while (a.StartsWith(TEXT("-")) || a.StartsWith(TEXT("/"))) a.RightChopInline(1);
				if (a.Equals(TEXT("capture"), ESearchCase::IgnoreCase) || a.StartsWith(TEXT("capture="), ESearchCase::IgnoreCase)) return true;
			}
			return false;
		}();
		return requested;
#else
		return false;
#endif
	}

	void Tick()
	{
		State& s = S();
		FrameFeed::TickStats();
		if (s.ShutDown || !Requested()) return;
		if (!s.Tried)
		{
			// Under -RenderOffScreen (no window) or -capture-source=frames, the game's own frames.
			if (FrameFeed::Wanted())
			{
				FString frames;
				if (!FrameFeed::Open(frames)) return; // the game viewport isn't up yet
				Start(FString::Printf(TEXT("--frames %s"), *frames));
				if (!s.Proc.IsValid()) FrameFeed::Close();
			}
			else
			{
				void* hwnd = GameWindowHandle();
				if (!hwnd) return; // the game window isn't up yet
				Start(FString::Printf(TEXT("--hwnd 0x%llx"), static_cast<unsigned long long>(reinterpret_cast<UPTRINT>(hwnd))));
			}
		}
		Pump();
		FrameFeed::Tick();
	}

	bool Ready() { return !Requested() || S().IsReady; }
	bool Any() { return S().IsAny; }
	bool Enabled() { return S().IsEnabled && !S().ShutDown; }
	const FString& SessionFolder() { return S().Folder; }

	void Raise(const FString& eventName, const Data& data)
	{
		if (!Enabled()) return;
		Send(FString::Printf(TEXT("{\"op\":\"raise\",\"event\":%s,\"data\":%s}"), *JsonString(eventName), *data.ToJson()));
	}

	void Marker(const FString& name, const Data& data)
	{
		if (!Enabled()) return;
		Send(FString::Printf(TEXT("{\"op\":\"marker\",\"name\":%s,\"data\":%s}"), *JsonString(name), *data.ToJson()));
	}

	void Command(const FString& command)
	{
		if (!Enabled()) return;
		Send(FString::Printf(TEXT("{\"op\":\"command\",\"command\":%s}"), *JsonString(command)));
	}

	void Note(const FString& line)
	{
		if (!Enabled()) return;
		Send(FString::Printf(TEXT("{\"op\":\"note\",\"text\":%s}"), *JsonString(line)));
	}

	int32 Screenshot(const FString& tag, const Data& data, const FString& setDir, const FString& caption)
	{
		FString fields = FString::Printf(TEXT(",\"tag\":%s,\"data\":%s"), *JsonString(tag), *data.ToJson());
		if (!setDir.IsEmpty()) fields += FString::Printf(TEXT(",\"set\":%s"), *JsonString(setDir));
		if (!caption.IsEmpty()) fields += FString::Printf(TEXT(",\"caption\":%s"), *JsonString(caption));
		return Request(TEXT("screenshot"), fields);
	}

	bool ScreenshotResult(int32 id, bool& ok, FString& fileOrError)
	{
		TArray<FString> files;
		FString error;
		if (!Result(id, ok, files, error)) return false;
		fileOrError = ok && files.Num() > 0 ? files[0] : error;
		return true;
	}

	int32 StartRecording(const FString& tag, const Data& data)
	{
		return Request(TEXT("record-start"), FString::Printf(TEXT(",\"tag\":%s,\"data\":%s"), *JsonString(tag), *data.ToJson()));
	}

	int32 StopRecording() { return Request(TEXT("record-stop")); }
	int32 PauseRecording() { return Request(TEXT("record-pause")); }
	int32 ResumeRecording() { return Request(TEXT("record-resume")); }

	int32 Clip(double before, double after, const FString& tag, const Data& data)
	{
		FString fields = FString::Printf(TEXT(",\"after\":%s,\"tag\":%s,\"data\":%s"), *FString::SanitizeFloat(after, 0), *JsonString(tag), *data.ToJson());
		if (before > 0) fields += FString::Printf(TEXT(",\"before\":%s"), *FString::SanitizeFloat(before, 0));
		return Request(TEXT("clip"), fields);
	}

	int32 SaveReplay(double before, double after, const FString& tag, const Data& data)
	{
		FString fields = FString::Printf(TEXT(",\"after\":%s,\"tag\":%s,\"data\":%s"), *FString::SanitizeFloat(after, 0), *JsonString(tag), *data.ToJson());
		if (before > 0) fields += FString::Printf(TEXT(",\"before\":%s"), *FString::SanitizeFloat(before, 0));
		return Request(TEXT("save-replay"), fields);
	}

	bool Result(int32 id, bool& ok, TArray<FString>& files, FString& error)
	{
		State& s = S();
		files.Reset();
		error.Reset();
		if (id == 0)
		{
			ok = false;
			error = TEXT("capture is off");
			return true;
		}
		Pump();
		State::FResult r;
		if (s.Results.RemoveAndCopyValue(id, r))
		{
			ok = r.Ok;
			files = MoveTemp(r.Files);
			error = MoveTemp(r.Error);
			return true;
		}
		// The host is gone: nothing more will be answered.
		if (!s.Proc.IsValid() || !FPlatformProcess::IsProcRunning(s.Proc))
		{
			ok = false;
			error = TEXT("the capture host is not running");
			return true;
		}
		return false;
	}

	FString NewPreviewSet(const FString& title, const FString& ticket)
	{
		FString args = FString::Printf(TEXT("set new --title %s --json"), *QuoteArg(title));
		if (!ticket.IsEmpty()) args += FString::Printf(TEXT(" --ticket %s"), *QuoteArg(ticket));
		FString out, dir;
		if (!RunAgent(args, out)) return FString();
		TSharedPtr<FJsonObject> manifest;
		if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(out), manifest) || !manifest.IsValid() || !manifest->TryGetStringField(TEXT("dir"), dir))
			UE_LOG(LogMindAtticCapture, Error, TEXT("[MindAttic.Media.Capture] `capture set new` printed no set folder: %s"), *out);
		return dir;
	}

	TArray<FString> AddToPreviewSet(const FString& dir, const TArray<FString>& files, const FString& tag, const FString& caption)
	{
		TArray<FString> added;
		if (files.IsEmpty()) return added;
		FString args = FString::Printf(TEXT("set add %s"), *QuoteArg(dir));
		for (const FString& f : files) args += TEXT(" ") + QuoteArg(f);
		if (!tag.IsEmpty()) args += FString::Printf(TEXT(" --tag %s"), *QuoteArg(tag));
		if (!caption.IsEmpty()) args += FString::Printf(TEXT(" --caption %s"), *QuoteArg(caption));
		FString out;
		if (RunAgent(args, out)) out.ParseIntoArrayLines(added);
		for (FString& line : added) line.TrimStartAndEndInline();
		return added;
	}

	FString ShowPreviewSet(const FString& dir)
	{
		FString out;
		return RunAgent(FString::Printf(TEXT("set show %s"), *QuoteArg(dir)), out) ? out.TrimStartAndEnd() : FString();
	}

	void Shutdown()
	{
		State& s = S();
		if (s.ShutDown) return;
		Send(TEXT("{\"op\":\"quit\"}"));
		s.ShutDown = true;
		// The host exports the session on its own (handed to its detached finalize), so the game never waits on it.
		if (s.StdinWrite) FPlatformProcess::ClosePipe(nullptr, s.StdinWrite);
		if (s.StdoutRead) FPlatformProcess::ClosePipe(s.StdoutRead, nullptr);
		s.StdinWrite = s.StdoutRead = nullptr;
		if (s.Proc.IsValid()) FPlatformProcess::CloseProc(s.Proc);
		FrameFeed::Close();
	}
}
