#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"

#include "MindAtticCaptureEditorSettings.generated.h"

/// Editor Preferences > Plugins > Media Capture (Play In Editor): this user's capture modes for Play-In-Editor, what
/// Tools > MindAttic > Capture toggles. The editor's counterpart of the Unity package's MINDATTIC_CAPTURE_EDITOR.
UCLASS(Config = EditorPerProjectUserSettings, meta = (DisplayName = "Media Capture (Play In Editor)"))
class UMindAtticCaptureEditorSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	/// Play-In-Editor sessions record video (-capture video).
	UPROPERTY(Config, EditAnywhere, Category = "Play In Editor")
	bool bRecordVideo = false;

	/// Play-In-Editor sessions take a screenshot every ScreenshotSeconds (-capture screen N).
	UPROPERTY(Config, EditAnywhere, Category = "Play In Editor")
	bool bScreenshots = false;

	UPROPERTY(Config, EditAnywhere, Category = "Play In Editor", meta = (ClampMin = "1", EditCondition = "bScreenshots"))
	int32 ScreenshotSeconds = 5;

	/// The -capture modes these stand for ("video", "screen 5", both), or empty for none.
	FString Modes() const
	{
		TArray<FString> words;
		if (bRecordVideo) words.Add(TEXT("video"));
		if (bScreenshots) words.Add(FString::Printf(TEXT("screen %d"), FMath::Max(1, ScreenshotSeconds)));
		return FString::Join(words, TEXT(" "));
	}

	virtual FName GetContainerName() const override { return TEXT("Editor"); }
	virtual FName GetCategoryName() const override { return TEXT("Plugins"); }
};
