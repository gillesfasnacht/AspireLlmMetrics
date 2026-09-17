using AspireLlmMetrics.AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

// --- 1. Metrics API (.NET) ---
var apiService = builder.AddProject<Projects.AspireLlmMetrics_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

// --- 2. LOKI (Logs) ---
var loki = builder.AddContainer("loki", "grafana/loki", "3.7.6")
    .WithBindMount("./config/loki/loki-config.yml", "/etc/loki/loki-config.yml", isReadOnly: true)
    .WithVolume("loki-data", "/var/loki")
    .WithArgs("-config.file=/etc/loki/loki-config.yml")
    .WithEndpoint(3100, targetPort: 3100)
    .WaitFor(apiService);

// --- 3. TEMPO (Traces) ---
var tempo = builder.AddContainer("tempo", "grafana/tempo", "3.0.2")
    .WithBindMount("./config/tempo/tempo-config.yml", "/etc/tempo/tempo-config.yml", isReadOnly: true)
    .WithVolume("tempo-data", "/var/tempo")
    .WithArgs("-config.file=/etc/tempo/tempo-config.yml")
    .WithEndpoint(3200, targetPort: 3200)   // Tempo HTTP API
    .WithEndpoint(4317, targetPort: 4317)   // OTLP gRPC
    .WithEndpoint(4318, targetPort: 4318)   // OTLP http
    .WaitFor(apiService);

// --- 4. PROMETHEUS (Métriques) version 3.13 LTS ---
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.13.1")
    .WithBindMount("./config/prometheus/prometheus.yml", "/etc/prometheus/prometheus.yml", isReadOnly: true)
    .WithVolume("prometheus-data", "/prometheus")
    .WithHttpEndpoint(port: 9090, targetPort: 9090)
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml", "--web.enable-otlp-receiver")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Prometheus Dashboard")
    .WaitFor(apiService);

// --- 5. OPENTELEMETRY COLLECTOR (Optimisé pour du full OLTP protocole) ---
var otelCollector = builder.AddOpenTelemetryCollector("otel-collector", "./config/otelcollector/otel-config.yml")
    .WaitFor(loki)
    .WaitFor(tempo)
    .WaitFor(prometheus);

// --- 6. GRAFANA ---
var grafana = builder.AddContainer("grafana", "grafana/grafana", "13.1.3")
    .WithBindMount("./config/grafana/grafana.ini", "/etc/grafana/grafana.ini", isReadOnly: true)
    .WithBindMount("./config/grafana/datasource.yml", "/etc/grafana/provisioning/datasources/datasource.yml", isReadOnly: true)
    .WithBindMount("./config/grafana/dashboard.yml", "/etc/grafana/provisioning/dashboards/dashboard.yml", isReadOnly: true)
    .WithBindMount("./config/grafana/dashboard/ASP.NET-Core.json", "/etc/grafana/provisioning/dashboards/ASP.NET-Core.json")
    .WithBindMount("./config/grafana/dashboard/ASP.NET-Core-Endpoint.json", "/etc/grafana/provisioning/dashboards/ASP.NET-Core-Endpoint.json")
    .WithBindMount("./config/grafana/dashboard/ASP.NET-Core-Logs.json", "/etc/grafana/provisioning/dashboards/ASP.NET-Core-Logs.json")
    .WithVolume("grafana-data", "/var/lib/grafana/")
    .WithContainerRuntimeArgs("--user", "0") // <--- C'est cette ligne qui donne les droits root dans le conteneur en cas de erreur avec Docker
    .WithHttpEndpoint(port: 3000, targetPort: 3000)
    .WithUrlForEndpoint("http", u => u.DisplayText = "Grafana Dashboard")
    .WaitFor(prometheus)
    .WaitFor(otelCollector)
    .WaitFor(tempo);

builder.Build().Run();