# Aspire.Hosting.Yarp

This is a sample project that shows how use YARP in development to route between multiple services.

## Getting Started

There are 2 ways to use the YARP resource:

1. Code based routes

```C#
var builder = DistributedApplication.CreateBuilder(args);

var app1 = builder.AddProject<Projects.WebApplication1>("app1");

var app2 = builder.AddProject<Projects.WebApplication2>("app2");

builder.AddYarp("ingress")
       .WithEndpoint(hostPort: 8001, scheme: "http")
       .Route("app1", path: "/app1", target: app1)
       .Route("app2", path: "/app2", target: app2);

builder.Build().Run();
```

2. Configuration based routes

```C#
var builder = DistributedApplication.CreateBuilder(args);

var app1 = builder.AddProject<Projects.WebApplication1>("app1");

var app2 = builder.AddProject<Projects.WebApplication2>("app2");

builder.AddYarp("ingress")
    .WithEndpoint(hostPort: 8001, scheme: "http")
    .WithReference(app1)
    .WithReference(app2)
    .LoadFromConfiguration("ReverseProxy");

builder.Build().Run();
```

```JSON
{
  "ReverseProxy": {
    "Routes": {
      "app1": {
        "ClusterId": "app1",
        "Match": {
          "Path": "/app1"
        },
        "Transforms": [
          { "PathRemovePrefix": "/app1" }
        ]
      },
      "app2": {
        "ClusterId": "app2",
        "Match": {
          "Path": "/app2"
        },
        "Transforms": [
          { "PathRemovePrefix": "/app2" }
        ]
      }
    },
    "Clusters": {
      "app1": {
        "Destinations": {
          "app1": {
            "Address": "http://app1"
          }
        }
      },
      "app2": {
        "Destinations": {
          "app2": {
            "Address": "http://app2"
          }
        }
      }
    }
  }
}
```

