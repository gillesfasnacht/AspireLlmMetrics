using AspireLlmMetrics.AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

// --- 1a. Metrics API (.NET) ---
var apiService = builder.AddProject<Projects.AspireLlmMetrics_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

// --- 1b. Web Frontend (.NET) ---
var webFrontend = builder.AddProject<Projects.AspireLlmMetrics_Web>("webfrontend")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithHttpHealthCheck("/health");

// --- 2. LOKI (Storage) ---
//
// Prepare the named volume for Loki's non-root user (including existing empty volumes).
// This is required because the Loki container runs as a non-root user (UID 10001).
// The init container will change the ownership of the volume to UID 10001 before Loki starts.
//
var lokiStorageInit = builder.AddContainer("loki-storage-init", "mcr.microsoft.com/azurelinux/base/core", "3.0")
    .WithVolume("loki-data", "/var/loki")
    .WithEntrypoint("/usr/bin/chown")
    .WithArgs("10001:10001", "/var/loki");

// --- 3. LOKI (Logs) ---
//
// Loki repository : https://github.com/grafana/loki/releases
//
var loki = builder.AddContainer("loki", "grafana/loki", "3.7.8")
    .WithBindMount("./config/loki/loki-config.yml", "/etc/loki/loki-config.yml", isReadOnly: true)
    .WithVolume("loki-data", "/var/loki")
    .WithArgs("-config.file=/etc/loki/loki-config.yml")
    .WithEndpoint(3100, targetPort: 3100)
    .WaitForCompletion(lokiStorageInit)
    .WaitFor(apiService);

// --- 4. TEMPO (Traces) ---
//
// Tempo repository : https://github.com/grafana/tempo/releases
//
var tempo = builder.AddContainer("tempo", "grafana/tempo", "3.0.4")
    .WithBindMount("./config/tempo/tempo-config.yml", "/etc/tempo/tempo-config.yml", isReadOnly: true)
    .WithVolume("tempo-data", "/var/tempo")
    .WithArgs("-config.file=/etc/tempo/tempo-config.yml")
    .WithEndpoint(3200, targetPort: 3200)   // Tempo HTTP API
    .WithEndpoint(4317, targetPort: 4317)   // OTLP gRPC
    .WithEndpoint(4318, targetPort: 4318)   // OTLP http
    .WaitFor(apiService);

// --- 5. PROMETHEUS (Metrics) version 3.13.2 LTS ---
//
// Prometheus repository : https://github.com/prometheus/prometheus/releases
//
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.13.4")
    .WithBindMount("./config/prometheus/prometheus.yml", "/etc/prometheus/prometheus.yml", isReadOnly: true)
    .WithVolume("prometheus-data", "/prometheus")
    .WithHttpEndpoint(port: 9090, targetPort: 9090)
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml", "--web.enable-otlp-receiver", "--web.enable-remote-write-receiver")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Prometheus Dashboard")
    .WaitFor(apiService);

// --- 6. OPENTELEMETRY COLLECTOR (Optimised for full OLTP protocole) ---
//
// OpenTelemetry Collector repository : https://github.com/open-telemetry/opentelemetry-collector/releases
//
var otelCollector = builder.AddOpenTelemetryCollector("otel-collector", "./config/otelcollector/otel-config.yml")
    .WaitFor(loki)
    .WaitFor(tempo)
    .WaitFor(prometheus);

// --- 7. GRAFANA ---
//
// Grafana repository : https://github.com/grafana/grafana/releases
//
var grafana = builder.AddContainer("grafana", "grafana/grafana", "13.2.4")
    .WithBindMount("./config/grafana/grafana.ini", "/etc/grafana/grafana.ini", isReadOnly: true)
    .WithBindMount("./config/grafana/datasource.yml", "/etc/grafana/provisioning/datasources/datasource.yml", isReadOnly: true)
    .WithBindMount("./config/grafana/dashboard.yml", "/etc/grafana/provisioning/dashboards/dashboard.yml", isReadOnly: true)
    .WithBindMount("./config/grafana/dashboard/ASP.NET-Core.json", "/etc/grafana/provisioning/dashboards/ASP.NET-Core.json")
    .WithBindMount("./config/grafana/dashboard/ASP.NET-Core-Endpoint.json", "/etc/grafana/provisioning/dashboards/ASP.NET-Core-Endpoint.json")
    .WithBindMount("./config/grafana/dashboard/ASP.NET-Core-Logs.json", "/etc/grafana/provisioning/dashboards/ASP.NET-Core-Logs.json")
    .WithBindMount("./config/grafana/dashboard/LLM.json", "/etc/grafana/provisioning/dashboards/LLM.json", isReadOnly: true)
    .WithVolume("grafana-data", "/var/lib/grafana/")
    .WithContainerRuntimeArgs("--user", "0") // <--- C'est cette ligne qui donne les droits root dans le conteneur en cas de erreur avec Docker
    .WithHttpEndpoint(port: 3000, targetPort: 3000)
    .WithUrlForEndpoint("http", u => u.DisplayText = "Grafana Dashboard")
    .WaitFor(prometheus)
    .WaitFor(otelCollector)
    .WaitFor(tempo);

builder.Build().Run();