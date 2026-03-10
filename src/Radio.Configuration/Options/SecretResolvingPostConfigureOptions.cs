using Microsoft.Extensions.Options;
using Radio.Configuration.Abstractions;
using System.Reflection;

namespace Radio.Configuration.Options;

/// <summary>
/// Post-configure options to resolve secret tags in string properties.
/// </summary>
/// <typeparam name="TOptions">The options type to resolve.</typeparam>
public class SecretResolvingPostConfigureOptions<TOptions> : IPostConfigureOptions<TOptions>
    where TOptions : class
{
    private readonly ISecretsProvider _secretsProvider;

    public SecretResolvingPostConfigureOptions(ISecretsProvider secretsProvider)
    {
        _secretsProvider = secretsProvider;
    }

    public void PostConfigure(string? name, TOptions options)
    {
        ResolveSecretsRecursive(options);
    }

    private void ResolveSecretsRecursive(object obj)
    {
        if (obj == null)
        {
            return;
        }

        var type = obj.GetType();
        if (type.IsPrimitive || type == typeof(string) || type.IsValueType)
        {
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead)
            {
                continue;
            }

            if (prop.PropertyType == typeof(string) && prop.CanWrite)
            {
                var value = (string?)prop.GetValue(obj);
                if (!string.IsNullOrEmpty(value) && _secretsProvider.ContainsSecretTag(value))
                {
                    // Synchronous wait is necessary here as PostConfigure is synchronous.
                    // This is acceptable during startup configuration.
                    var resolved = _secretsProvider.ResolveTagsAsync(value).GetAwaiter().GetResult();
                    prop.SetValue(obj, resolved);
                }
            }
            else if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
            {
                var value = prop.GetValue(obj);
                if (value != null)
                {
                    ResolveSecretsRecursive(value);
                }
            }
        }
    }
}
