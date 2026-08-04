using System.Reflection;
using HarmonyLib;
using Xunit;

namespace MultiplayerColors.Tests;

/// <summary>
/// Every patch in this mod names its target method as a string or via <c>nameof</c> on a game type, and
/// Harmony binds each patch parameter to a target parameter by *name*. Both are silent failures at runtime:
/// a renamed method logs one line during startup and then the mod simply does nothing.
///
/// These tests resolve the same things Harmony resolves, so a game update that moves the ground out from
/// under a patch fails the build instead of shipping a mod that quietly stops working.
/// </summary>
public class PatchTargetTests
{
    // Harmony's injected parameters; they don't correspond to target parameters.
    private static readonly string[] InjectedPrefixes = ["__instance", "__result", "__state", "__originalMethod", "__args", "__exception", "__runOriginal", "___"];

    public static TheoryData<Type> PatchClasses
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var type in typeof(MainFile).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                {
                    data.Add(type);
                }
            }

            return data;
        }
    }

    [Fact]
    public void EveryPatchFileIsDiscovered()
    {
        // A sanity floor: if the reflection above silently finds nothing, the rest of this file passes
        // vacuously.
        Assert.True(PatchClasses.Count >= 13, $"expected at least 13 patch classes, found {PatchClasses.Count}");
    }

    [Theory]
    [MemberData(nameof(PatchClasses))]
    public void PatchTargetResolves(Type patchClass)
    {
        Assert.NotNull(ResolveTarget(patchClass));
    }

    [Theory]
    [MemberData(nameof(PatchClasses))]
    public void PatchParametersMatchTheTargetSignature(Type patchClass)
    {
        var target = ResolveTarget(patchClass);
        Assert.NotNull(target);

        var targetParams = target!.GetParameters().ToDictionary(p => p.Name!, p => p.ParameterType);

        foreach (var patchMethod in PatchMethods(patchClass))
        {
            foreach (var param in patchMethod.GetParameters())
            {
                var name = param.Name!;
                if (InjectedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                Assert.True(
                    targetParams.ContainsKey(name),
                    $"{patchClass.Name}.{patchMethod.Name} takes '{name}', which is not a parameter of {target.DeclaringType!.Name}.{target.Name}");

                Assert.True(
                    targetParams[name].IsAssignableFrom(param.ParameterType) || param.ParameterType.IsAssignableFrom(targetParams[name]),
                    $"{patchClass.Name}.{patchMethod.Name} declares '{name}' as {param.ParameterType.Name}, but the target has {targetParams[name].Name}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(PatchClasses))]
    public void PatchClassDeclaresAtLeastOnePatchMethod(Type patchClass)
    {
        Assert.NotEmpty(PatchMethods(patchClass));
    }

    /// <summary>
    /// Resolves the patched method the same way Harmony does: merge every <c>[HarmonyPatch]</c> attribute on
    /// the class, then look the method up on the declaring type.
    /// </summary>
    private static MethodBase? ResolveTarget(Type patchClass)
    {
        var infos = patchClass
            .GetCustomAttributes(typeof(HarmonyPatch), true)
            .Cast<HarmonyPatch>()
            .Select(a => a.info)
            .ToList();

        var merged = HarmonyMethod.Merge(infos);
        Assert.NotNull(merged.declaringType);
        Assert.False(string.IsNullOrEmpty(merged.methodName), $"{patchClass.Name} names no target method");

        return AccessTools.Method(merged.declaringType!, merged.methodName!, merged.argumentTypes);
    }

    private static List<MethodInfo> PatchMethods(Type patchClass) => patchClass
        .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m =>
            m.GetCustomAttribute<HarmonyPrefix>() != null ||
            m.GetCustomAttribute<HarmonyPostfix>() != null)
        .ToList();
}
