#pragma once

#include "CoreMinimal.h"

namespace MindAttic::Media::Capture::AgentLocator
{
	/// capture.exe, in order: MINDATTIC_CAPTURE_HOST, MINDATTIC_CAPTURE_AGENT, the package's Capture\capture.exe,
	/// %LOCALAPPDATA%\MindAttic\Capture\bin\capture.exe, PATH. Only a file whose ProductName is the agent's counts, as
	/// UnityAgentLocator does. Empty when none is found.
	FString Find();

	/// True when <paramref name="path"/> is a capture.exe published as MindAttic.Media.Capture.Agent (or the old
	/// MindAttic.Capture.Agent).
	bool IsAgent(const FString& path);
}
