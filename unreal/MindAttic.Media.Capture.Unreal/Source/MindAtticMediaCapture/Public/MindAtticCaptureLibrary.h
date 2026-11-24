#pragma once

#include "CoreMinimal.h"
#include "Containers/Ticker.h"
#include "Kismet/BlueprintAsyncActionBase.h"
#include "Kismet/BlueprintFunctionLibrary.h"

#include "MindAtticCaptureLibrary.generated.h"

/// MindAttic.Media.Capture for Blueprints (MindAtticCapture.h is the C++ API). Requests return an id, 0 when capture
/// is off; "Wait For Capture Result" waits on one.
UCLASS(meta = (ScriptName = "MindAtticCapture"))
class MINDATTICMEDIACAPTURE_API UMindAtticCaptureLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/// -capture is on and the capture engine is running.
	UFUNCTION(BlueprintPure, Category = "MindAttic|Capture")
	static bool IsCaptureRunning();

	/// This run's capture folder (empty when capture is off).
	UFUNCTION(BlueprintPure, Category = "MindAttic|Capture")
	static FString SessionFolder();

	/// A game event for the capture.json rules; Data feeds their "where" conditions and the sidecars.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture", meta = (AutoCreateRefTerm = "Data"))
	static void Raise(const FString& Event, const TMap<FString, FString>& Data);

	/// A chapter in the exported video.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture", meta = (AutoCreateRefTerm = "Data"))
	static void Marker(const FString& Name, const TMap<FString, FString>& Data);

	/// Starts a recording; its result is ok once recording.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static int32 StartRecording(const FString& Tag = TEXT("recording"));

	/// Stops the recording; its result names the exported files.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static int32 StopRecording();

	/// Leaves footage out of the recording until ResumeRecording.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static int32 PauseRecording();

	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static int32 ResumeRecording();

	/// Saves the replay ring: the last Before seconds (0: the session's replay length) and After more.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static int32 SaveReplay(float Before = 0, float After = 0, const FString& Tag = TEXT("replay"));

	/// A clip of Before seconds of the past (needs the replay ring; 0 for none) and After seconds from now.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static int32 Clip(float Before = 0, float After = 10, const FString& Tag = TEXT("clip"));

	/// A new preview set (capture set new): its folder, or empty when the capture agent is missing or failed.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static FString NewPreviewSet(const FString& Title, const FString& Ticket);

	/// Copies Files into the preview set (one item named Tag, else one per file) and returns the set's copies.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static TArray<FString> AddToPreviewSet(const FString& SetDir, const TArray<FString>& Files, const FString& Tag, const FString& Caption);

	/// Opens the preview set in Media.Viewer; false when the agent failed.
	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture")
	static bool ShowPreviewSet(const FString& SetDir);
};

DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FMindAtticCaptureFileDone, const FString&, File);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FMindAtticCaptureFilesDone, const TArray<FString>&, Files);
DECLARE_DYNAMIC_MULTICAST_DELEGATE_OneParam(FMindAtticCaptureFailed, const FString&, Error);

/// Polls a capture request's result each frame until it is answered.
UCLASS(Abstract)
class MINDATTICMEDIACAPTURE_API UMindAtticCaptureWaitAction : public UBlueprintAsyncActionBase
{
	GENERATED_BODY()

public:
	virtual void Activate() override;
	virtual void BeginDestroy() override;

protected:
	int32 RequestId = 0;

	virtual void Done(bool bOk, const TArray<FString>& Files, const FString& Error) {}

private:
	FTSTicker::FDelegateHandle TickHandle;

	bool Poll(float DeltaTime);
	void StopPolling();
};

/// Takes a screenshot, optionally into a preview set with a caption; OnDone gives the file.
UCLASS()
class MINDATTICMEDIACAPTURE_API UMindAtticCaptureScreenshotAction : public UMindAtticCaptureWaitAction
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintAssignable)
	FMindAtticCaptureFileDone OnDone;

	UPROPERTY(BlueprintAssignable)
	FMindAtticCaptureFailed OnFailed;

	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture",
		meta = (BlueprintInternalUseOnly = "true", WorldContext = "WorldContextObject", DisplayName = "Take Capture Screenshot", AutoCreateRefTerm = "Data"))
	static UMindAtticCaptureScreenshotAction* TakeCaptureScreenshot(UObject* WorldContextObject, const FString& Tag, const TMap<FString, FString>& Data,
		const FString& SetDir, const FString& Caption);

protected:
	virtual void Done(bool bOk, const TArray<FString>& Files, const FString& Error) override;
};

/// Waits on the id a recording, clip or replay request returned; OnDone gives its files.
UCLASS()
class MINDATTICMEDIACAPTURE_API UMindAtticCaptureResultAction : public UMindAtticCaptureWaitAction
{
	GENERATED_BODY()

public:
	UPROPERTY(BlueprintAssignable)
	FMindAtticCaptureFilesDone OnDone;

	UPROPERTY(BlueprintAssignable)
	FMindAtticCaptureFailed OnFailed;

	UFUNCTION(BlueprintCallable, Category = "MindAttic|Capture",
		meta = (BlueprintInternalUseOnly = "true", WorldContext = "WorldContextObject", DisplayName = "Wait For Capture Result"))
	static UMindAtticCaptureResultAction* WaitForCaptureResult(UObject* WorldContextObject, int32 RequestId);

protected:
	virtual void Done(bool bOk, const TArray<FString>& Files, const FString& Error) override;
};
