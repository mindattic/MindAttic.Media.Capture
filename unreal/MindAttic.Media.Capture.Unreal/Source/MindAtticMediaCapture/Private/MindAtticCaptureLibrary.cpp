#include "MindAtticCaptureLibrary.h"

#include "MindAtticCapture.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(MindAtticCaptureLibrary)

namespace Capture = MindAttic::Media::Capture;

namespace
{
	Capture::Data ToData(const TMap<FString, FString>& map)
	{
		Capture::Data data;
		for (const TPair<FString, FString>& field : map) data.Str(*field.Key, field.Value);
		return data;
	}
}

bool UMindAtticCaptureLibrary::IsCaptureRunning() { return Capture::Enabled(); }
FString UMindAtticCaptureLibrary::SessionFolder() { return Capture::SessionFolder(); }
void UMindAtticCaptureLibrary::Raise(const FString& Event, const TMap<FString, FString>& Data) { Capture::Raise(Event, ToData(Data)); }
void UMindAtticCaptureLibrary::Marker(const FString& Name, const TMap<FString, FString>& Data) { Capture::Marker(Name, ToData(Data)); }
int32 UMindAtticCaptureLibrary::StartRecording(const FString& Tag) { return Capture::StartRecording(Tag); }
int32 UMindAtticCaptureLibrary::StopRecording() { return Capture::StopRecording(); }
int32 UMindAtticCaptureLibrary::PauseRecording() { return Capture::PauseRecording(); }
int32 UMindAtticCaptureLibrary::ResumeRecording() { return Capture::ResumeRecording(); }
int32 UMindAtticCaptureLibrary::SaveReplay(float Before, float After, const FString& Tag) { return Capture::SaveReplay(Before, After, Tag); }
int32 UMindAtticCaptureLibrary::Clip(float Before, float After, const FString& Tag) { return Capture::Clip(Before, After, Tag); }
FString UMindAtticCaptureLibrary::NewPreviewSet(const FString& Title, const FString& Ticket) { return Capture::NewPreviewSet(Title, Ticket); }

TArray<FString> UMindAtticCaptureLibrary::AddToPreviewSet(const FString& SetDir, const TArray<FString>& Files, const FString& Tag, const FString& Caption)
{
	return Capture::AddToPreviewSet(SetDir, Files, Tag, Caption);
}

bool UMindAtticCaptureLibrary::ShowPreviewSet(const FString& SetDir) { return !Capture::ShowPreviewSet(SetDir).IsEmpty(); }

void UMindAtticCaptureWaitAction::Activate()
{
	TickHandle = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateUObject(this, &UMindAtticCaptureWaitAction::Poll));
}

void UMindAtticCaptureWaitAction::BeginDestroy()
{
	StopPolling();
	Super::BeginDestroy();
}

bool UMindAtticCaptureWaitAction::Poll(float DeltaTime)
{
	bool ok = false;
	TArray<FString> files;
	FString error;
	if (!Capture::Result(RequestId, ok, files, error)) return true;
	TickHandle.Reset(); // returning false removes the ticker
	Done(ok, files, error);
	SetReadyToDestroy();
	return false;
}

void UMindAtticCaptureWaitAction::StopPolling()
{
	if (TickHandle.IsValid()) FTSTicker::GetCoreTicker().RemoveTicker(TickHandle);
	TickHandle.Reset();
}

UMindAtticCaptureScreenshotAction* UMindAtticCaptureScreenshotAction::TakeCaptureScreenshot(UObject* WorldContextObject, const FString& Tag,
	const TMap<FString, FString>& Data, const FString& SetDir, const FString& Caption)
{
	UMindAtticCaptureScreenshotAction* action = NewObject<UMindAtticCaptureScreenshotAction>();
	action->RequestId = Capture::Screenshot(Tag, ToData(Data), SetDir, Caption);
	action->RegisterWithGameInstance(WorldContextObject);
	return action;
}

void UMindAtticCaptureScreenshotAction::Done(bool bOk, const TArray<FString>& Files, const FString& Error)
{
	if (bOk) OnDone.Broadcast(Files.Num() > 0 ? Files[0] : FString());
	else OnFailed.Broadcast(Error);
}

UMindAtticCaptureResultAction* UMindAtticCaptureResultAction::WaitForCaptureResult(UObject* WorldContextObject, int32 RequestId)
{
	UMindAtticCaptureResultAction* action = NewObject<UMindAtticCaptureResultAction>();
	action->RequestId = RequestId;
	action->RegisterWithGameInstance(WorldContextObject);
	return action;
}

void UMindAtticCaptureResultAction::Done(bool bOk, const TArray<FString>& Files, const FString& Error)
{
	if (bOk) OnDone.Broadcast(Files);
	else OnFailed.Broadcast(Error);
}
