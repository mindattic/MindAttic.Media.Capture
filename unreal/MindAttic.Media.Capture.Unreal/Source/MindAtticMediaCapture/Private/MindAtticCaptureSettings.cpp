#include "MindAtticCaptureSettings.h"

#include "HAL/FileManager.h"
#include "Misc/App.h"
#include "Misc/Paths.h"

UMindAtticCaptureSettings::UMindAtticCaptureSettings()
{
	SectionName = TEXT("MediaCapture");
}

FString UMindAtticCaptureSettings::ResolveAppName() const
{
	if (!AppName.IsEmpty()) return AppName;
	// CaptureNaming.RepoName: the nearest folder holding a .git directory or file.
	FString dir = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
	FPaths::NormalizeDirectoryName(dir);
	while (!dir.IsEmpty())
	{
		const FString git = FPaths::Combine(dir, TEXT(".git"));
		if (IFileManager::Get().DirectoryExists(*git) || FPaths::FileExists(git)) return FPaths::GetCleanFilename(dir);
		const FString parent = FPaths::GetPath(dir);
		if (parent == dir) break;
		dir = parent;
	}
	return FApp::GetProjectName();
}

FString UMindAtticCaptureSettings::ResolveDefaultsFile() const
{
	if (DefaultsFile.IsEmpty()) return FString();
	return FPaths::ConvertRelativePathToFull(FPaths::Combine(FPaths::ProjectDir(), DefaultsFile));
}
