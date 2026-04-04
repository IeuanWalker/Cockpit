using System.Globalization;
using System.Runtime.InteropServices;

namespace Cockpit;

// Minimal COM interop for the Windows ISpellChecker API (spellcheck.h).
// CoreWebView2ContextMenuItem.Label is empty for spell suggestions in the WebView2 SDK
// version bundled with MAUI 10. We work around this by querying the OS spell checker.
static class WindowsSpellChecker
{
	// CLSID_SpellCheckerFactory from spellcheck.h
	static readonly Guid clsidFactory = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
	// IID_ISpellCheckerFactory
	static readonly Guid iidFactory = new("8E018A9D-2415-4677-BF08-794EA61F94BB");
	// IID_ISpellChecker
	static readonly Guid iidChecker = new("B6C0FD67-520C-4A34-B79E-9F0D3B47A9FD");
	// IID_IEnumString
	static readonly Guid iidEnumString = new("00000101-0000-0000-C000-000000000046");

	const uint CLSCTX_INPROC_SERVER = 1;
	const uint CLSCTX_LOCAL_SERVER = 4;

	[DllImport("ole32.dll")]
	static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

	[DllImport("ole32.dll")]
	static extern int CoTaskMemFree(IntPtr pv);

	// Vtable delegates — offsets are 0-based after the 3 IUnknown slots.
	// ISpellCheckerFactory vtable (slot 3 = index 0 after IUnknown):
	//   [3] get_SupportedLanguages, [4] IsSupported, [5] CreateSpellChecker
	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	delegate int IsSupported_Fn(IntPtr pThis, [MarshalAs(UnmanagedType.LPWStr)] string lang, out int supported);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	delegate int CreateSpellChecker_Fn(IntPtr pThis, [MarshalAs(UnmanagedType.LPWStr)] string lang, out IntPtr checker);

	// ISpellChecker vtable:
	//   [3]=get_LanguageTag, [4]=Check, [5]=Suggest, ...
	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	delegate int Suggest_Fn(IntPtr pThis, [MarshalAs(UnmanagedType.LPWStr)] string word, out IntPtr enumStr);

	// IEnumString vtable:
	//   [3]=Next, [4]=Skip, [5]=Reset, [6]=Clone
	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	delegate int EnumNext_Fn(IntPtr pThis, uint celt, IntPtr rgelt, IntPtr pceltFetched);

	static T GetVtableMethod<T>(IntPtr pObj, int slotIndex) where T : Delegate
	{
		IntPtr vtable = Marshal.ReadIntPtr(pObj);
		IntPtr slot = Marshal.ReadIntPtr(vtable + slotIndex * IntPtr.Size);
		return Marshal.GetDelegateForFunctionPointer<T>(slot);
	}

	public static List<string> GetSuggestions(string word)
	{
		var result = new List<string>();
		IntPtr pFactory = IntPtr.Zero;
		IntPtr pChecker = IntPtr.Zero;
		IntPtr pEnum = IntPtr.Zero;
		IntPtr pWord = IntPtr.Zero;
		IntPtr pFetched = IntPtr.Zero;

		try
		{
			// Create the factory via CoCreateInstance with both in-proc and local server contexts
			Guid clsid = clsidFactory, iid = iidFactory;
			int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER, ref iid, out pFactory);
			if (hr != 0 || pFactory == IntPtr.Zero)
			{
				return result;
			}

			var isSupported = GetVtableMethod<IsSupported_Fn>(pFactory, 4);
			var createChecker = GetVtableMethod<CreateSpellChecker_Fn>(pFactory, 5);

			string[] candidates = [CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name, "en-GB", "en-US"];
			foreach (string lang in candidates.Distinct())
			{
				int hr2 = isSupported(pFactory, lang, out int supported);
				if (hr2 == 0 && supported != 0)
				{
					int hr3 = createChecker(pFactory, lang, out pChecker);
					if (hr3 == 0 && pChecker != IntPtr.Zero)
					{
						break;
					}
				}
			}

			if (pChecker == IntPtr.Zero)
			{
				return result;
			}

			var suggest = GetVtableMethod<Suggest_Fn>(pChecker, 5);
			int hrSuggest = suggest(pChecker, word, out pEnum);
			if (hrSuggest != 0 || pEnum == IntPtr.Zero)
			{
				return result;
			}

			var enumNext = GetVtableMethod<EnumNext_Fn>(pEnum, 3);

			// Allocate buffers for IEnumString.Next: one LPOLESTR slot + one ULONG for fetched count
			pWord = Marshal.AllocCoTaskMem(IntPtr.Size);
			pFetched = Marshal.AllocCoTaskMem(sizeof(uint));

			while (true)
			{
				Marshal.WriteIntPtr(pWord, IntPtr.Zero);
				Marshal.WriteInt32(pFetched, 0);

				int hrNext = enumNext(pEnum, 1, pWord, pFetched);

				uint fetched = (uint)Marshal.ReadInt32(pFetched);
				if (fetched == 0)
				{
					break;
				}

				IntPtr strPtr = Marshal.ReadIntPtr(pWord);
				if (strPtr != IntPtr.Zero)
				{
					string s = Marshal.PtrToStringUni(strPtr) ?? "";
					CoTaskMemFree(strPtr);
					if (!string.IsNullOrWhiteSpace(s))
					{
						result.Add(s);
					}
				}

				if (hrNext != 0) // S_FALSE = no more items
				{
					break;
				}
			}
		}
		catch
		{
			// COM errors are non-fatal — return whatever we collected
		}
		finally
		{
			if (pFetched != IntPtr.Zero) Marshal.FreeCoTaskMem(pFetched);
			if (pWord != IntPtr.Zero) Marshal.FreeCoTaskMem(pWord);
			if (pEnum != IntPtr.Zero) Marshal.Release(pEnum);
			if (pChecker != IntPtr.Zero) Marshal.Release(pChecker);
			if (pFactory != IntPtr.Zero) Marshal.Release(pFactory);
		}

		return result;
	}
}
