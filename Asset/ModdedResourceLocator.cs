using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace MehToolBox;

public class ModdedResourceLocator : UnityEngine.Object
{
    public ModdedResourceLocator() : base(ClassInjector.DerivedConstructorPointer<ModdedResourceLocator>()) => ClassInjector.DerivedConstructorBody(this);

    public bool Locate(Il2CppSystem.Object key, Il2CppSystem.Type type, out Il2CppSystem.Collections.Generic.IList<IResourceLocation> locations)
    {
        if (!CustomAddressablesPatch.customAddressablePaths.ContainsKey(key.ToString()))
        {
            locations = new Il2CppSystem.Collections.Generic.List<IResourceLocation>().Cast<Il2CppSystem.Collections.Generic.IList<IResourceLocation>>();
            return false;
        }
        MelonLogger.Msg("Locating anything?: " + key.ToString());

        var resourceLocators = new Il2CppSystem.Collections.Generic.List<IResourceLocation>();
        resourceLocators.Add(new ResourceLocationBase(key.ToString(), key.ToString(), typeof(BundledAssetProvider).FullName, type).Cast<IResourceLocation>());
        locations = resourceLocators.Cast<Il2CppSystem.Collections.Generic.IList<IResourceLocation>>();

        return true;
    }

    public string LocatorId => "Ok";

    public Il2CppSystem.Collections.Generic.IEnumerable<Il2CppSystem.Object> Keys =>
        new Il2CppSystem.Collections.Generic.List<UnityEngine.Object>().Cast<Il2CppSystem.Collections.Generic.IEnumerable<Il2CppSystem.Object>>();
}


[HarmonyPatch]
public static class CustomAddressablesPatch
{
    public static Dictionary<string, UnityEngine.Object> customAddressablePaths = new Dictionary<string, UnityEngine.Object>();

    [HarmonyPatch(typeof(ResourceManager), nameof(ResourceManager.ProvideResource), typeof(IResourceLocation), typeof(Il2CppSystem.Type), typeof(bool))]
    [HarmonyPrefix]
    public static bool ProvideResourcePrefix(ResourceManager __instance, IResourceLocation location, Il2CppSystem.Type desiredType, ref AsyncOperationHandle __result)
    {
        if (location == null)
            return true;

        if (!location.PrimaryKey.StartsWith("Modded"))
            return true;

        var firstOrDefault = customAddressablePaths[location.PrimaryKey];
        string systemTypeName = desiredType.FullName;
        if (!systemTypeName.StartsWith("UnityEngine"))
        {
            if (desiredType.Namespace == null)
                systemTypeName = systemTypeName.Insert(0, "Il2Cpp.");
            else
                systemTypeName = systemTypeName.Insert(0, "Il2Cpp");
        }

        MethodInfo method = AccessTools.Method(typeof(Il2CppObjectBase), "Cast", new Type[0], new[] { AccessTools.TypeByName(systemTypeName) });
        object result = method.Invoke(firstOrDefault, null);

        MethodInfo inf = AccessTools.Method(typeof(ResourceManager), "CreateCompletedOperationInternal");
        MethodInfo genInf = inf.MakeGenericMethod(AccessTools.TypeByName(systemTypeName));

        var completedOperationInternal = genInf.Invoke(__instance, new[] { result, true, null, false });

        var asyncOperationHandle = new AsyncOperationHandle(((Il2CppObjectBase)AccessTools.PropertyGetter(completedOperationInternal.GetType(), "InternalOp")
            .Invoke(completedOperationInternal, null)).Cast<IAsyncOperation>(), (int)AccessTools.PropertyGetter(completedOperationInternal.GetType(),
            "m_Version").Invoke(completedOperationInternal, null)); // this is the worst thing ever actually
        __result = asyncOperationHandle;
        return false;
    }

    // Game has failsafes in order to prevent loading invalid assets, bypass them
    [HarmonyPatch(typeof(AssetReference), "RuntimeKeyIsValid")]
    [HarmonyPrefix]
    public static bool LabelModdedKeysAsValid(AssetReference __instance, ref bool __result)
    {
        if (customAddressablePaths.ContainsKey(__instance.RuntimeKey.ToString()))
        {
            __result = true;
            return false;
        }
        return true;
    }
}
