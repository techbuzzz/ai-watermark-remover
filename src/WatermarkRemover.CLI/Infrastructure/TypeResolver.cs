using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>Resolves types from a built <see cref="IServiceProvider"/> for Spectre.Console.Cli.</summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    public object? Resolve(Type? type) => type is null ? null : _provider.GetService(type);

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
