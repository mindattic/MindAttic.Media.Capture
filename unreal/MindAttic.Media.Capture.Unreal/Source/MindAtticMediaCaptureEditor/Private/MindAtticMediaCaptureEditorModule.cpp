#include "MindAtticCapture.h"
#include "MindAtticCaptureEditorSettings.h"
#include "MindAtticCaptureSettings.h"
#include "MindAtticMediaCaptureModule.h"

#include "Editor.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformProcess.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "ToolMenus.h"

#define LOCTEXT_NAMESPACE "MindAtticMediaCaptureEditor"

namespace MindAttic::Media::Capture::Editor
{
	namespace
	{
		bool Playing() { return GEditor && GEditor->PlayWorld != nullptr; }

		/// R:\MindAttic\Media\Capture\<AppName>; else the legacy R:\MindAttic\Capture\<AppName> when only that one exists (LEGACY-ROOT);
		/// else the capture root when the app has no folder yet.
		FString CaptureRoot()
		{
			const FString root = TEXT("R:\\MindAttic\\Media\\Capture");
			const FString appName = GetDefault<UMindAtticCaptureSettings>()->ResolveAppName();
			const FString app = FPaths::Combine(root, appName);
			if (IFileManager::Get().DirectoryExists(*app)) return app;
			const FString legacy = FPaths::Combine(TEXT("R:\\MindAttic\\Capture"), appName); // LEGACY-ROOT
			if (IFileManager::Get().DirectoryExists(*legacy)) return legacy; // LEGACY-ROOT
			return root;
		}

		void OpenFolder()
		{
			const FString session = SessionFolder();
			const FString folder = Enabled() && IFileManager::Get().DirectoryExists(*session) ? session : CaptureRoot();
			if (IFileManager::Get().DirectoryExists(*folder)) FPlatformProcess::ExploreFolder(*folder);
			else UE_LOG(LogMindAtticCapture, Warning, TEXT("[MindAttic.Media.Capture] %s does not exist yet."), *folder);
		}

		void OpenLastPreviewSet()
		{
			const FString app = GetDefault<UMindAtticCaptureSettings>()->ResolveAppName();
			const FString dir = LastPreviewSet(app);
			if (dir.IsEmpty())
			{
				UE_LOG(LogMindAtticCapture, Warning, TEXT("[MindAttic.Media.Capture] %s has no preview set yet."), *app);
				return;
			}
			ShowPreviewSet(dir);
		}

		void Toggle(bool UMindAtticCaptureEditorSettings::* flag)
		{
			UMindAtticCaptureEditorSettings* settings = GetMutableDefault<UMindAtticCaptureEditorSettings>();
			settings->*flag = !(settings->*flag);
			settings->SaveConfig();
		}

		FUIAction ToggleAction(bool UMindAtticCaptureEditorSettings::* flag)
		{
			return FUIAction(
				FExecuteAction::CreateLambda([flag] { Toggle(flag); }),
				FCanExecuteAction::CreateLambda([] { return !Playing(); }),
				FGetActionCheckState::CreateLambda([flag]
				{
					return GetDefault<UMindAtticCaptureEditorSettings>()->*flag ? ECheckBoxState::Checked : ECheckBoxState::Unchecked;
				}));
		}

		void FillCapture(UToolMenu* menu)
		{
			FToolMenuSection& pie = menu->AddSection(TEXT("PlayInEditor"), LOCTEXT("PlayInEditor", "Play In Editor"));
			pie.AddMenuEntry(TEXT("RecordVideo"),
				LOCTEXT("RecordVideo", "Record Video In Play"),
				LOCTEXT("RecordVideoTip", "Play-In-Editor sessions record video, as -capture video does for the game."),
				FSlateIcon(), ToggleAction(&UMindAtticCaptureEditorSettings::bRecordVideo), EUserInterfaceActionType::ToggleButton);
			pie.AddMenuEntry(TEXT("Screenshots"),
				TAttribute<FText>::CreateLambda([]
				{
					return FText::Format(LOCTEXT("Screenshots", "Screenshot Every {0} s In Play"), GetDefault<UMindAtticCaptureEditorSettings>()->ScreenshotSeconds);
				}),
				LOCTEXT("ScreenshotsTip", "Play-In-Editor sessions take a screenshot every few seconds, as -capture screen N does (the interval is in Editor Preferences, Plugins, Media Capture)."),
				FSlateIcon(), ToggleAction(&UMindAtticCaptureEditorSettings::bScreenshots), EUserInterfaceActionType::ToggleButton);
			pie.AddMenuEntry(TEXT("TakeScreenshot"),
				LOCTEXT("TakeScreenshot", "Take Screenshot"),
				LOCTEXT("TakeScreenshotTip", "A screenshot of the running Play-In-Editor session into its capture folder."),
				FSlateIcon(),
				FUIAction(
					FExecuteAction::CreateLambda([] { Screenshot(TEXT("editor")); }),
					FCanExecuteAction::CreateLambda([] { return Playing() && Enabled(); })));

			FToolMenuSection& output = menu->AddSection(TEXT("Output"), LOCTEXT("Output", "Output"));
			output.AddMenuEntry(TEXT("OpenCaptureFolder"),
				LOCTEXT("OpenCaptureFolder", "Open Capture Folder"),
				LOCTEXT("OpenCaptureFolderTip", "The running session's folder, else R:\\MindAttic\\Media\\Capture\\<AppName> (or an older R:\\MindAttic\\Capture\\<AppName> when only that exists)."), // LEGACY-ROOT
				FSlateIcon(), FUIAction(FExecuteAction::CreateStatic(&OpenFolder)));
			output.AddMenuEntry(TEXT("OpenLastPreviewSet"),
				LOCTEXT("OpenLastPreviewSet", "Open Last Preview Set"),
				LOCTEXT("OpenLastPreviewSetTip", "Opens this app's newest preview set in Media.Viewer (capture set show)."),
				FSlateIcon(), FUIAction(FExecuteAction::CreateStatic(&OpenLastPreviewSet)));
		}
	}
}

/// Tools > MindAttic > Capture and Play-In-Editor capture: the Unity package's CaptureMenu for the Unreal editor.
class FMindAtticMediaCaptureEditorModule : public IModuleInterface
{
public:
	virtual void StartupModule() override
	{
		UToolMenus::RegisterStartupCallback(FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &FMindAtticMediaCaptureEditorModule::RegisterMenus));
		PostPIEStartedHandle = FEditorDelegates::PostPIEStarted.AddLambda([](bool simulating)
		{
			if (!simulating) MindAttic::Media::Capture::BeginEditorSession(GetDefault<UMindAtticCaptureEditorSettings>()->Modes());
		});
		EndPIEHandle = FEditorDelegates::EndPIE.AddLambda([](bool) { MindAttic::Media::Capture::EndEditorSession(); });
	}

	virtual void ShutdownModule() override
	{
		FEditorDelegates::PostPIEStarted.Remove(PostPIEStartedHandle);
		FEditorDelegates::EndPIE.Remove(EndPIEHandle);
		UToolMenus::UnRegisterStartupCallback(this);
		UToolMenus::UnregisterOwner(this);
		MindAttic::Media::Capture::EndEditorSession();
	}

private:
	void RegisterMenus()
	{
		FToolMenuOwnerScoped owner(this);
		UToolMenu* tools = UToolMenus::Get()->ExtendMenu(TEXT("LevelEditor.MainMenu.Tools"));
		FToolMenuSection& section = tools->FindOrAddSection(TEXT("MindAttic"), LOCTEXT("MindAtticSection", "MindAttic"));
		section.AddSubMenu(TEXT("MindAttic"),
			LOCTEXT("MindAtticMenu", "MindAttic"),
			LOCTEXT("MindAtticMenuTip", "MindAttic tools."),
			FNewToolMenuDelegate::CreateLambda([](UToolMenu* menu)
			{
				menu->AddSection(TEXT("MindAttic")).AddSubMenu(TEXT("Capture"),
					LOCTEXT("CaptureMenu", "Capture"),
					LOCTEXT("CaptureMenuTip", "MindAttic.Media.Capture: Play-In-Editor capture, screenshots, the capture folder and preview sets."),
					FNewToolMenuDelegate::CreateStatic(&MindAttic::Media::Capture::Editor::FillCapture));
			}));
	}

	FDelegateHandle PostPIEStartedHandle;
	FDelegateHandle EndPIEHandle;
};

IMPLEMENT_MODULE(FMindAtticMediaCaptureEditorModule, MindAtticMediaCaptureEditor)

#undef LOCTEXT_NAMESPACE
