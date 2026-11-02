using System.Reflection;
using Wip.Bones.Agents.Knowledge;

namespace Wip.Bones.Web.Tests;

internal static class BonesPoolTestHelper
{
    private static readonly FieldInfo? s_instanceField = typeof(StrategyPool)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
        ?? typeof(StrategyPool).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);

    public static StrategyPool? GetInstance() => StrategyPool.Instance;

    public static void SetInstance(StrategyPool? value)
    {
        if (s_instanceField is not null)
        {
            s_instanceField.SetValue(null, value);
        }
        else
        {
            // Fallback: try the constructor which sets Instance = this
            if (value is not null)
            {
                // StrategyPool constructor sets Instance = this internally.
                // We can't trigger it externally without calling the constructor.
                // Use the backup field name approach
                var field = typeof(StrategyPool).GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(null, value);
                    return;
                }
            }

            // Last resort: set via private property setter
            var prop = typeof(StrategyPool).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetSetMethod(true) is MethodInfo setter)
            {
                setter.Invoke(null, new object?[] { value });
            }
        }
    }
}
