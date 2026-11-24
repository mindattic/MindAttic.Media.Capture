#include "MindAtticMediaCaptureModule.h"

#include "MindAtticCapture.h"

#include "Containers/Ticker.h"
#include "Misc/CoreDelegates.h"
#include "Modules/ModuleManager.h"

DEFINE_LOG_CATEGORY(LogMindAtticCapture);

/// Ticks the capture client from the core ticker (no game actor needed) and stops it when the process exits.
class FMindAtticMediaCaptureModule : public IModuleInterface
{
public:
	virtual void StartupModule() override
	{
		if (!MindAttic::Media::Capture::Requested()) return;
		TickHandle = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateLambda([](float)
		{
			MindAttic::Media::Capture::Tick();
			return true;
		}));
		PreExitHandle = FCoreDelegates::OnPreExit.AddStatic(&MindAttic::Media::Capture::Shutdown);
	}

	virtual void ShutdownModule() override
	{
		if (TickHandle.IsValid()) FTSTicker::GetCoreTicker().RemoveTicker(TickHandle);
		if (PreExitHandle.IsValid()) FCoreDelegates::OnPreExit.Remove(PreExitHandle);
		TickHandle.Reset();
		PreExitHandle.Reset();
		MindAttic::Media::Capture::Shutdown();
	}

private:
	FTSTicker::FDelegateHandle TickHandle;
	FDelegateHandle PreExitHandle;
};

IMPLEMENT_MODULE(FMindAtticMediaCaptureModule, MindAtticMediaCapture)
