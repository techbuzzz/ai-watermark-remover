using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace WatermarkRemover.CLI.Infrastructure;

/// <summary>Bridges <see cref="IServiceCollection"/> to Spectre.Console.Cli's <see cref="ITypeRegistrar"/>.</summary>
public sealed class TypeRegistrar(IServiceCollection builder) : ITypeRegistrar
{
    private readonly IServiceCollection _builder = builder;

    public ITypeResolver Build() => new TypeResolver(_builder.BuildServiceProvider());

    public void Register(Type service, Type implementation) => _builder.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) => _builder.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _builder.AddSingleton(service, _ => factory());
    }
}
