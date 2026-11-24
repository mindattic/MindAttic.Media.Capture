#include "MindAtticCaptureAgentLocator.h"

#include "HAL/PlatformMisc.h"
#include "Misc/Paths.h"

#if PLATFORM_WINDOWS
#include "Windows/AllowWindowsPlatformTypes.h"
#include <winver.h>
#include "Windows/HideWindowsPlatformTypes.h"
#endif

namespace MindAttic::Media::Capture::AgentLocator
{
	namespace
	{
		const TCHAR* ProductName = TEXT("MindAttic.Media.Capture.Agent");
		const TCHAR* LegacyProductName = TEXT("MindAttic.Capture.Agent");

		FString ReadProductName(const FString& path)
		{
#if PLATFORM_WINDOWS
			DWORD handle = 0;
			const DWORD size = ::GetFileVersionInfoSizeW(*path, &handle);
			if (size == 0) return FString();
			TArray<uint8> info;
			info.SetNumZeroed(size);
			if (!::GetFileVersionInfoW(*path, 0, size, info.GetData())) return FString();

			struct FTranslation { WORD Language; WORD CodePage; };
			FTranslation* translations = nullptr;
			UINT bytes = 0;
			if (!::VerQueryValueW(info.GetData(), TEXT("\\VarFileInfo\\Translation"), reinterpret_cast<void**>(&translations), &bytes))
				return FString();
			for (UINT i = 0; i < bytes / sizeof(FTranslation); ++i)
			{
				const FString key = FString::Printf(TEXT("\\StringFileInfo\\%04x%04x\\ProductName"), translations[i].Language, translations[i].CodePage);
				TCHAR* value = nullptr;
				UINT length = 0;
				if (::VerQueryValueW(info.GetData(), *key, reinterpret_cast<void**>(&value), &length) && value && length > 0)
					return FString(value);
			}
#endif
			return FString();
		}
	}

	bool IsAgent(const FString& path)
	{
		if (path.IsEmpty() || !FPaths::FileExists(path)) return false;
		const FString product = ReadProductName(path);
		return product == ProductName || product == LegacyProductName;
	}

	FString Find()
	{
		TArray<FString> candidates;
		for (const TCHAR* variable : { TEXT("MINDATTIC_CAPTURE_HOST"), TEXT("MINDATTIC_CAPTURE_AGENT") })
		{
			const FString env = FPlatformMisc::GetEnvironmentVariable(variable);
			if (!env.IsEmpty()) candidates.Add(env);
		}
		candidates.Add(FPaths::Combine(FPaths::RootDir(), TEXT("Capture"), TEXT("capture.exe")));
		const FString localAppData = FPlatformMisc::GetEnvironmentVariable(TEXT("LOCALAPPDATA"));
		if (!localAppData.IsEmpty())
			candidates.Add(FPaths::Combine(localAppData, TEXT("MindAttic"), TEXT("Capture"), TEXT("bin"), TEXT("capture.exe")));
		TArray<FString> dirs;
		FPlatformMisc::GetEnvironmentVariable(TEXT("PATH")).ParseIntoArray(dirs, TEXT(";"), true);
		for (FString dir : dirs)
		{
			dir.TrimStartAndEndInline();
			dir.TrimQuotesInline();
			if (!dir.IsEmpty()) candidates.Add(FPaths::Combine(dir, TEXT("capture.exe")));
		}
		for (const FString& c : candidates)
		{
			const FString full = FPaths::ConvertRelativePathToFull(c);
			if (IsAgent(full)) return full;
		}
		return FString();
	}
}
