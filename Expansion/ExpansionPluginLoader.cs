using System.Reflection;
using System.Runtime.Loader;

namespace OldSimulator.Expansion;

public sealed class ExpansionPluginLoadException : Exception
{
    public ExpansionPluginLoadException(string message) : base(message)
    {
    }

    public ExpansionPluginLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ExpansionPluginLoader
{
    public static IReadOnlyList<ExpansionCardInstallation> Load(string configPath)
    {
        ExpansionConfiguration configuration = ExpansionConfiguration.Load(configPath);
        var installations = new List<ExpansionCardInstallation>(configuration.Slots.Count);
        var plugins = new Dictionary<string, LoadedPlugin>(PathComparer);

        try
        {
            foreach (ExpansionSlotConfiguration slot in configuration.Slots.OrderBy(slot => slot.Slot))
            {
                if (!plugins.TryGetValue(slot.AssemblyPath, out LoadedPlugin? plugin))
                {
                    plugin = LoadedPlugin.Load(slot.AssemblyPath);
                    plugins.Add(slot.AssemblyPath, plugin);
                }

                ExpansionCardDescriptor descriptor = plugin.FindCard(slot.CardId);
                IExpansionCard card = plugin.CreateCard(slot);
                installations.Add(new ExpansionCardInstallation(slot.Slot, descriptor, card));
            }

            return installations.AsReadOnly();
        }
        catch
        {
            for (var index = installations.Count - 1; index >= 0; index--)
            {
                try
                {
                    installations[index].Card.Dispose();
                }
                catch
                {
                    // Preserve the load failure; every wrapper still releases its load-context lease.
                }
            }

            foreach (LoadedPlugin plugin in plugins.Values)
                plugin.UnloadIfUnused();

            throw;
        }
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class LoadedPlugin
    {
        private readonly IExpansionCardPlugin factory;
        private readonly IReadOnlyDictionary<string, ExpansionCardDescriptor> descriptors;
        private readonly PluginContextLease lease;

        private LoadedPlugin(
            IExpansionCardPlugin factory,
            IReadOnlyDictionary<string, ExpansionCardDescriptor> descriptors,
            PluginContextLease lease)
        {
            this.factory     = factory;
            this.descriptors = descriptors;
            this.lease       = lease;
        }

        public static LoadedPlugin Load(string assemblyPath)
        {
            if (!File.Exists(assemblyPath))
                throw new ExpansionPluginLoadException($"Expansion plugin assembly '{assemblyPath}' does not exist.");

            var context = new ExpansionPluginLoadContext(assemblyPath);
            var lease   = new PluginContextLease(context);
            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
                Type[] factories = assembly.GetExportedTypes()
                    .Where(type => type is { IsClass: true, IsAbstract: false } &&
                                   typeof(IExpansionCardPlugin).IsAssignableFrom(type) &&
                                   type.GetConstructor(Type.EmptyTypes) is not null)
                    .ToArray();

                if (factories.Length != 1)
                {
                    throw new ExpansionPluginLoadException(
                        $"Expansion plugin assembly '{assemblyPath}' must expose exactly one public, " +
                        $"parameterless IExpansionCardPlugin factory; found {factories.Length}.");
                }

                IExpansionCardPlugin factory;
                try
                {
                    factory = (IExpansionCardPlugin)(Activator.CreateInstance(factories[0]) ??
                              throw new InvalidOperationException("The plugin factory constructor returned null."));
                }
                catch (Exception error) when (error is not ExpansionPluginLoadException)
                {
                    Exception cause = unwrapInvocation(error);
                    throw new ExpansionPluginLoadException(
                        $"Expansion plugin factory '{factories[0].FullName}' in '{assemblyPath}' could not be created: " +
                        cause.Message,
                        cause);
                }

                if (factory.ApiVersion != ExpansionCardApi.Version)
                {
                    throw new ExpansionPluginLoadException(
                        $"Expansion plugin assembly '{assemblyPath}' uses API version {factory.ApiVersion}; " +
                        $"expected {ExpansionCardApi.Version}.");
                }

                IReadOnlyList<ExpansionCardDescriptor> cards = factory.Cards ??
                    throw new ExpansionPluginLoadException(
                        $"Expansion plugin assembly '{assemblyPath}' returned a null card descriptor list.");
                var descriptors = new Dictionary<string, ExpansionCardDescriptor>(StringComparer.Ordinal);
                foreach (ExpansionCardDescriptor descriptor in cards)
                {
                    if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.Id))
                    {
                        throw new ExpansionPluginLoadException(
                            $"Expansion plugin assembly '{assemblyPath}' contains a card descriptor without an ID.");
                    }
                    if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
                    {
                        throw new ExpansionPluginLoadException(
                            $"Expansion plugin assembly '{assemblyPath}' card '{descriptor.Id}' has no display name.");
                    }
                    if (!descriptors.TryAdd(descriptor.Id, descriptor))
                    {
                        throw new ExpansionPluginLoadException(
                            $"Expansion plugin assembly '{assemblyPath}' declares duplicate card ID '{descriptor.Id}'.");
                    }
                }

                return new LoadedPlugin(factory, descriptors, lease);
            }
            catch (ExpansionPluginLoadException)
            {
                lease.UnloadIfUnused();
                throw;
            }
            catch (Exception error)
            {
                lease.UnloadIfUnused();
                Exception cause = unwrapInvocation(error);
                throw new ExpansionPluginLoadException(
                    $"Expansion plugin assembly '{assemblyPath}' could not be loaded: {cause.Message}",
                    cause);
            }
        }

        public ExpansionCardDescriptor FindCard(string cardId)
        {
            if (descriptors.TryGetValue(cardId, out ExpansionCardDescriptor? descriptor))
                return descriptor;

            throw new ExpansionPluginLoadException(
                $"Expansion plugin does not declare configured card ID '{cardId}'.");
        }

        public IExpansionCard CreateCard(ExpansionSlotConfiguration slot)
        {
            try
            {
                IExpansionCard card = factory.Create(
                    slot.CardId,
                    new ExpansionCardCreateContext(slot.Slot),
                    slot.Settings) ?? throw new InvalidOperationException("The plugin returned a null card instance.");
                return lease.Wrap(card);
            }
            catch (Exception error) when (error is not ExpansionPluginLoadException)
            {
                Exception cause = unwrapInvocation(error);
                throw new ExpansionPluginLoadException(
                    $"Expansion plugin could not create card '{slot.CardId}' for slot {slot.Slot}: {cause.Message}",
                    cause);
            }
        }

        public void UnloadIfUnused()
        {
            lease.UnloadIfUnused();
        }
    }

    private sealed class LoadedExpansionCard(IExpansionCard inner, PluginContextLease lease) : IExpansionCard
    {
        private IExpansionCard? inner = inner;

        public void BeginCommand(ushort command, Memory<byte> mailbox, IExpansionCardCommand completion)
        {
            current().BeginCommand(command, mailbox, completion);
        }

        public void AdvanceCycles(ulong cycles)
        {
            current().AdvanceCycles(cycles);
        }

        public void Reset()
        {
            current().Reset();
        }

        public void Dispose()
        {
            IExpansionCard? card = Interlocked.Exchange(ref inner, null);
            if (card is null)
                return;

            try
            {
                card.Dispose();
            }
            finally
            {
                lease.Release();
            }
        }

        private IExpansionCard current()
        {
            return Volatile.Read(ref inner) ??
                   throw new ObjectDisposedException(nameof(LoadedExpansionCard));
        }
    }

    private sealed class PluginContextLease(ExpansionPluginLoadContext context)
    {
        private int cards;
        private int unloaded;

        public IExpansionCard Wrap(IExpansionCard card)
        {
            Interlocked.Increment(ref cards);
            return new LoadedExpansionCard(card, this);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref cards) == 0)
                unload();
        }

        public void UnloadIfUnused()
        {
            if (Volatile.Read(ref cards) == 0)
                unload();
        }

        private void unload()
        {
            if (Interlocked.Exchange(ref unloaded, 1) == 0)
                context.Unload();
        }
    }

    private sealed class ExpansionPluginLoadContext : AssemblyLoadContext
    {
        private static readonly Assembly ContractAssembly = typeof(IExpansionCardPlugin).Assembly;
        private static readonly AssemblyName ContractAssemblyName = ContractAssembly.GetName();

        private readonly AssemblyDependencyResolver resolver;

        public ExpansionPluginLoadContext(string assemblyPath)
            : base($"OldSimulator expansion: {Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: true)
        {
            resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, ContractAssemblyName))
                return ContractAssembly;

            string? path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }

    private static Exception unwrapInvocation(Exception error)
    {
        return error is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!
            : error;
    }
}
