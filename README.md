# AspireLlmMetrics

Application .NET 10 / Aspire : interface Blazor, chat Ollama via Agent Framework,
traces Tempo, logs Loki et métriques Prometheus visualisés dans Grafana.

## Démarrer

Prérequis : SDK .NET 10, Docker démarré et Ollama accessible sur la machine hôte.
Le modèle configuré par défaut est `granite4.2:8b` ; il doit être installé dans Ollama.
Configurer `LLM:Model` et `LLM:Endpoint` dans les fichiers appsettings de l'API,
ou via les variables `LLM__Model` et `LLM__Endpoint`.

```powershell
dotnet run --project AspireLlmMetrics.AppHost --launch-profile http
```

Ouvrir `webfrontend` depuis le dashboard Aspire. L'AppHost démarre également
l'API, le collecteur OpenTelemetry, Loki, Tempo, Prometheus et Grafana.
Le profil `https` nécessite un certificat de développement .NET approuvé.
Grafana est accessible sur http://localhost:3000 avec la configuration locale existante.
Le tableau de bord **LLM - Requêtes et tokens** est provisionné automatiquement.
Les premiers taux apparaissent après plusieurs exports de métriques.

## Routes de chat

- `POST /chat` : JSON `{ "prompt": "Bonjour" }`, réponse complète avec métriques.
- `POST /agents/chat` : protocole AG-UI en streaming, utilisé par l'interface.

Les deux routes utilisent le même client instrumenté. Les métriques comptent les
appels au fournisseur LLM : une tentative, puis un succès, une erreur ou une
annulation. Un futur agent exécutant plusieurs appels LLM comptera chaque appel.
Les annulations demandées par l'appelant et les flux abandonnés sont distincts des
erreurs ; un timeout du fournisseur sans annulation de l'appelant reste une erreur.
Les durées sont exprimées en secondes et portent le label `llm.outcome`.
Les tokens et débits sont enregistrés pour les appels terminés avec succès.
Le compteur de cold starts utilise l'heuristique d'un chargement supérieur à 100 ms.

Loki écrit dans le volume `loki-data` monté sur `/var/loki`. Un conteneur ponctuel prépare les droits du volume pour son utilisateur non-root (UID 10001).
Les anciens logs écrits dans `/tmp/loki` ne sont pas migrés automatiquement.
Tempo exporte ses métriques dérivées vers le récepteur remote write de Prometheus.

## Vérifier

```powershell
dotnet build AspireLlmMetrics.slnx
dotnet test AspireLlmMetrics.Tests
```

Les tests utilisent un fournisseur simulé : ils couvrent les routes JSON et AG-UI
avec le client du frontend, les tokens, les erreurs et les annulations, sans Docker
ni modèle Ollama. La solution inclut le frontend afin que sa compilation soit
vérifiée avec celle des autres projets.
