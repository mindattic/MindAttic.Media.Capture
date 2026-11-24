#pragma once

// MindAttic.Media.Capture for Unreal games: what the Unity package's CaptureUnity is to a Unity player. C++ can't load
// .NET, so on -capture the game starts the installed agent's `capture host`, hands it its window (or, under
// -RenderOffScreen or -capture-source=frames, its own frames through a shared-memory ring) and its command line,
// and drives it over a pipe: one JSON request a line on its stdin, one JSON reply or notice a line from its stdout
// (docs/design/engine-host.md). Off unless -capture is on the command line; -capture video / screen N /
// -capture-config path.json; output in R:\MindAttic\Capture\<AppName>\. The module ticks this itself and shuts it
// down on exit; the game only raises events, markers, screenshots and commands.

#include "CoreMinimal.h"

namespace MindAttic::Media::Capture
{
	/// A CaptureData bag: event data for the capture.json rules' "where" conditions and the sidecars.
	class MINDATTICMEDIACAPTURE_API Data
	{
	public:
		Data& Num(const TCHAR* key, double value);
		Data& Str(const TCHAR* key, const FString& value);
		Data& Bool(const TCHAR* key, bool value);
		/// The bag as a JSON object ("{}" when empty).
		FString ToJson() const;
		bool IsEmpty() const { return Fields.IsEmpty(); }

	private:
		/// Key and its value already in JSON.
		TArray<TPair<FString, FString>> Fields;
	};

	/// -capture (or -capture=...) is on the command line. Whether a mode was asked for is the host's to say (Any).
	MINDATTICMEDIACAPTURE_API bool Requested();

	/// Once a frame (the module's core ticker): starts the host when the game window (or viewport, for the frame feed)
	/// exists, reads its replies and feeds it frames when due.
	MINDATTICMEDIACAPTURE_API void Tick();

	/// The host has answered (started, or said why not): Any and Enabled are known.
	MINDATTICMEDIACAPTURE_API bool Ready();

	/// `CaptureUnity.Args.Any`: a capture mode was asked for (-capture alone is video).
	MINDATTICMEDIACAPTURE_API bool Any();

	/// `CaptureUnity.Engine.Enabled`: the engine is running.
	MINDATTICMEDIACAPTURE_API bool Enabled();

	/// `CaptureUnity.Engine.SessionFolder`.
	MINDATTICMEDIACAPTURE_API const FString& SessionFolder();

	/// `CaptureUnity.Raise`: a game event for the capture.json rules. Cheap; never blocks; nothing when capture is off.
	MINDATTICMEDIACAPTURE_API void Raise(const FString& eventName, const Data& data = Data());

	/// `CaptureUnity.Marker`: a chapter in the exported video.
	MINDATTICMEDIACAPTURE_API void Marker(const FString& name, const Data& data = Data());

	/// `CaptureUnity.Engine.ExecuteCommandAsync(command)`: "screenshot", "toggle-recording", "save-replay".
	MINDATTICMEDIACAPTURE_API void Command(const FString& command);

	/// A line of the game's log, kept in the session folder's .game.log: a Shipping game writes no log of its own.
	MINDATTICMEDIACAPTURE_API void Note(const FString& line);

	/// `CaptureUnity.Engine.ScreenshotAsync(tag)`: a request id (0 when capture is off) to wait on with ScreenshotResult.
	/// With `setDir` (a preview set folder, from NewPreviewSet) the shot is also copied into the set with `caption`, and
	/// the result names the set's copy.
	MINDATTICMEDIACAPTURE_API int32 Screenshot(const FString& tag, const Data& data = Data(), const FString& setDir = FString(), const FString& caption = FString());

	/// True once screenshot `id` is answered: `ok` and the file, or the error.
	MINDATTICMEDIACAPTURE_API bool ScreenshotResult(int32 id, bool& ok, FString& fileOrError);

	/// `StartRecordingAsync`: a request id (0 when capture is off); its result is ok once recording.
	MINDATTICMEDIACAPTURE_API int32 StartRecording(const FString& tag = TEXT("recording"), const Data& data = Data());

	/// `StopRecordingAsync`: its result names the exported files.
	MINDATTICMEDIACAPTURE_API int32 StopRecording();

	/// `PauseRecording`: footage until ResumeRecording is left out.
	MINDATTICMEDIACAPTURE_API int32 PauseRecording();

	/// `ResumeRecording`.
	MINDATTICMEDIACAPTURE_API int32 ResumeRecording();

	/// A clip of `before` seconds of the past (needs the replay ring; 0 for none) and `after` seconds from now.
	MINDATTICMEDIACAPTURE_API int32 Clip(double before = 0, double after = 10, const FString& tag = TEXT("clip"), const Data& data = Data());

	/// `SaveReplayAsync`: the last `before` seconds (0: the session's replay length) and `after` more.
	MINDATTICMEDIACAPTURE_API int32 SaveReplay(double before = 0, double after = 0, const FString& tag = TEXT("replay"), const Data& data = Data());

	/// True once request `id` (from any of the above) is answered: `ok` with its files, or the error. Results can
	/// arrive in any order; each is handed out once.
	MINDATTICMEDIACAPTURE_API bool Result(int32 id, bool& ok, TArray<FString>& files, FString& error);

	/// `capture set new --title --ticket --json`: a new preview set's folder, or empty when the agent is missing or failed.
	/// Runs the agent and waits for it (a fraction of a second); works whether or not -capture is on.
	MINDATTICMEDIACAPTURE_API FString NewPreviewSet(const FString& title, const FString& ticket = FString());

	/// `capture set add`: copies `files` into the set as its next item (one item named `tag`, else one per file) and
	/// returns the set's copies.
	MINDATTICMEDIACAPTURE_API TArray<FString> AddToPreviewSet(const FString& dir, const TArray<FString>& files, const FString& tag = FString(), const FString& caption = FString());

	/// `capture set show`: opens the set in Media.Viewer; what it did (pipe, launched, photos, none), or empty on failure.
	MINDATTICMEDIACAPTURE_API FString ShowPreviewSet(const FString& dir);

	/// The game is quitting: asks the host to stop and export (it also does when the game's process ends).
	MINDATTICMEDIACAPTURE_API void Shutdown();
}
