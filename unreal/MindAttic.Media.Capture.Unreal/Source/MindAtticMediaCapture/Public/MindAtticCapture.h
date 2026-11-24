#pragma once

// MindAttic.Media.Capture for Unreal games: what the Unity package's CaptureUnity is to a Unity player. C++ can't load
// .NET, so on -capture the game starts the installed agent's `capture host`, hands it its window and its command line,
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

	/// Once a frame (the module's core ticker): starts the host when the game window exists and reads its replies.
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
	MINDATTICMEDIACAPTURE_API int32 Screenshot(const FString& tag, const Data& data = Data());

	/// True once screenshot `id` is answered: `ok` and the file, or the error.
	MINDATTICMEDIACAPTURE_API bool ScreenshotResult(int32 id, bool& ok, FString& fileOrError);

	/// The game is quitting: asks the host to stop and export (it also does when the game's process ends).
	MINDATTICMEDIACAPTURE_API void Shutdown();
}
