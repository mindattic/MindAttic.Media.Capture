using UnrealBuildTool;

public class MindAtticMediaCapture : ModuleRules
{
	public MindAtticMediaCapture(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] {
			"Core",
			"CoreUObject",
			"Engine",
			"DeveloperSettings",
		});

		PrivateDependencyModuleNames.AddRange(new string[] {
			"Json",
			"Slate",
			"SlateCore",
			"ApplicationCore",
		});

		// The agent locator reads capture.exe's ProductName (GetFileVersionInfo).
		PublicSystemLibraries.Add("version.lib");
	}
}
