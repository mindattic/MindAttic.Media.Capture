using UnrealBuildTool;

public class MindAtticMediaCaptureEditor : ModuleRules
{
	public MindAtticMediaCaptureEditor(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PrivateDependencyModuleNames.AddRange(new string[] {
			"Core",
			"CoreUObject",
			"Engine",
			"DeveloperSettings",
			"UnrealEd",
			"ToolMenus",
			"Slate",
			"SlateCore",
			"MindAtticMediaCapture",
		});
	}
}
