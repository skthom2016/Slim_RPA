#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Namespace-pattern declaration of all action and predicate namespaces this worker supports, plus runtime limits.</summary>
/// <param name="ActionNamespaces">Glob-style namespace patterns covering all action types the worker can execute.</param>
/// <param name="PredicateNamespaces">Glob-style namespace patterns covering all predicate namespaces the worker can evaluate.</param>
/// <param name="MaxIterations">Maximum iteration count the worker enforces for control.repeatBounded steps.</param>
public record AdvertisedCapabilities(
    [property: JsonPropertyName("actionNamespaces")] IReadOnlyList<string> ActionNamespaces,
    [property: JsonPropertyName("predicateNamespaces")] IReadOnlyList<string> PredicateNamespaces,
    [property: JsonPropertyName("maxIterations")] int MaxIterations);
