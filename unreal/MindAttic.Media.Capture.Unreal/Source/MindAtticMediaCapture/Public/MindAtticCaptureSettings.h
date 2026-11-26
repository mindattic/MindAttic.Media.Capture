#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"

#include "MindAtticCaptureSettings.generated.h"

/// Project Settings > Plugins > Media Capture: what the game hands `capture host`.
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "Media Capture"))
class MINDATTICMEDIACAPTURE_API UMindAtticCaptureSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	UMindAtticCaptureSettings();

	/// The folder name under R:\MindAttic\Media\Capture. Empty: the repo folder holding the .uproject (CaptureNaming.RepoName),
	/// else the project name.
	UPROPERTY(Config, EditAnywhere, Category = "Capture")
	FString AppName;

	/// The game's shipped capture.json, relative to the project folder. Empty: none.
	UPROPERTY(Config, EditAnywhere, Category = "Capture")
	FString DefaultsFile;

	/// AppName, or the name it defaults to.
	FString ResolveAppName() const;

	/// The full path of DefaultsFile, or empty when none is set.
	FString ResolveDefaultsFile() const;

	virtual FName GetCategoryName() const override { return TEXT("Plugins"); }
};
