using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Publishing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Aspire.Hosting;

public static class YarpResoruceExtensions
{
    public static IResourceBuilder<YarpResource> AddYarp(this IDistributedApplicationBuilder builder, string name)
    {
        var yarp = builder.Resources.OfType<YarpResource>().SingleOrDefault();

        if (yarp is not null)
        {
            // You only need one yarp resource per application
            throw new InvalidOperationException("A yarp resource has already been added to this application");
        }

        builder.Services.TryAddLifecycleHook<YarpResourceLifecyclehook>();

        var resource = new YarpResource(name);
        return builder.AddResource(resource).ExcludeFromManifest();

        // REVIEW: YARP resource type?
        //.WithManifestPublishingCallback(context =>
        // {
        //     context.Writer.WriteString("type", "yarp.v0");

        //     context.Writer.WriteStartObject("routes");
        //     // REVIEW: Make this less YARP specific
        //     foreach (var r in resource.RouteConfigs.Values)
        //     {
        //         context.Writer.WriteStartObject(r.RouteId);

        //         context.Writer.WriteStartObject("match");
        //         context.Writer.WriteString("path", r.Match.Path);

        //         if (r.Match.Hosts is not null)
        //         {
        //             context.Writer.WriteStartArray("hosts");
        //             foreach (var h in r.Match.Hosts)
        //             {
        //                 context.Writer.WriteStringValue(h);
        //             }
        //             context.Writer.WriteEndArray();
        //         }
        //         context.Writer.WriteEndObject();
        //         context.Writer.WriteString("destination", r.ClusterId);
        //         context.Writer.WriteEndObject();
        //     }
        //     context.Writer.WriteEndObject();
        // });
    }

    public static IResourceBuilder<YarpResource> LoadFromConfiguration(this IResourceBuilder<YarpResource> builder, string sectionName)
    {
        builder.Resource.ConfigurationSectionName = sectionName;
        return builder;
    }

    public static IResourceBuilder<YarpResource> Route(this IResourceBuilder<YarpResource> builder, string routeId, IResourceBuilder<IResourceWithServiceDiscovery> target, string? path = null, string[]? hosts = null, bool preservePath = false)
    {
        builder.Resource.RouteConfigs[routeId] = new()
        {
            RouteId = routeId,
            ClusterId = target.Resource.Name,
            Match = new()
            {
                Path = path,
                Hosts = hosts
            },
            Transforms =
            [
                preservePath || path is null
                ? []
                : new Dictionary<string, string>{ ["PathRemovePrefix"] = path }
            ]
        };

        if (builder.Resource.ClusterConfigs.ContainsKey(target.Resource.Name))
        {
            return builder;
        }

        builder.Resource.ClusterConfigs[target.Resource.Name] = new()
        {
            ClusterId = target.Resource.Name,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [target.Resource.Name] = new() { Address = $"http://{target.Resource.Name}" }
            }
        };

        builder.WithReference(target);

        return builder;
    }
}

public class YarpResource(string name) : Resource(name), IResourceWithServiceDiscovery, IResourceWithEnvironment
{
    // YARP configuration
    internal Dictionary<string, RouteConfig> RouteConfigs { get; } = [];
    internal Dictionary<string, ClusterConfig> ClusterConfigs { get; } = [];
    internal List<EndpointAnnotation> Endpoints { get; } = [];
    internal string? ConfigurationSectionName { get; set; }
}

// This starts up the YARP reverse proxy with the configuration from the resource
internal class YarpResourceLifecyclehook(IOptions<PublishingOptions> options) : IDistributedApplicationLifecycleHook, IAsyncDisposable
{
    private WebApplication? _app;

    public Task BeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
    {
        if (options.Value.Publisher == "manifest")
        {
            return Task.CompletedTask;
        }

        var yarpResource = appModel.Resources.OfType<YarpResource>().SingleOrDefault();

        if (yarpResource is null)
        {
            return Task.CompletedTask;
        }

        // We don't want to create proxies for yarp resources so remove them
        var bindings = yarpResource.Annotations.OfType<EndpointAnnotation>().ToList();

        foreach (var b in bindings)
        {
            yarpResource.Annotations.Remove(b);
            yarpResource.Endpoints.Add(b);
        }

        return Task.CompletedTask;
    }

    public async Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
    {
        if (options.Value.Publisher == "manifest")
        {
            return;
        }

        var yarpResource = appModel.Resources.OfType<YarpResource>().SingleOrDefault();

        if (yarpResource is null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();

        // Convert environment variables into configuration
        if (yarpResource.TryGetEnvironmentVariables(out var envAnnotations))
        {
            var context = new EnvironmentCallbackContext(options.Value.Publisher!);

            foreach (var cb in envAnnotations)
            {
                cb.Callback(context);
            }

            var dict = new Dictionary<string, string?>();
            foreach (var (k, v) in context.EnvironmentVariables)
            {
                dict[k.Replace("__", ":")] = v;
            }

            builder.Configuration.AddInMemoryCollection(dict);
        }

        builder.Services.AddServiceDiscovery();

        var proxyBuilder = builder.Services.AddReverseProxy();

        if (yarpResource.RouteConfigs.Count > 0)
        {
            proxyBuilder.LoadFromMemory(yarpResource.RouteConfigs.Values.ToList(), yarpResource.ClusterConfigs.Values.ToList());
        }
        
        if (yarpResource.ConfigurationSectionName is not null)
        {
            proxyBuilder.LoadFromConfig(builder.Configuration.GetSection(yarpResource.ConfigurationSectionName));
        }

        proxyBuilder.AddServiceDiscoveryDestinationResolver();

        _app = builder.Build();

        if (yarpResource.Endpoints.Count == 0)
        {
            _app.Urls.Add($"http://127.0.0.1:0");
        }
        else
        {
            foreach (var ep in yarpResource.Endpoints)
            {
                var scheme = ep.UriScheme ?? "http";

                if (ep.Port is null)
                {
                    _app.Urls.Add($"{scheme}://127.0.0.1:0");
                }
                else
                {
                    _app.Urls.Add($"{scheme}://localhost:{ep.Port}");
                }
            }
        }

        _app.MapReverseProxy();

        await _app.StartAsync();
    }

    public ValueTask DisposeAsync()
    {
        return _app?.DisposeAsync() ?? default;
    }
}