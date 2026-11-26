using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Domain;

namespace HookRunner.Infrastructure;

/// <summary>Conserve les compteurs dans un document JSON unique, remplacé atomiquement.</summary>
public sealed class FileHookStateStore : IHookStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <inheritdoc />
    public async Task<IHookStateSession> OpenAsync(string stateFilePath, HookIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string path = CanonicalPath.Resolve(stateFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new HookIdentity(CanonicalPath.Resolve(identity.SolutionRoot),
                identity.Agent, identity.SessionId, identity.HookName).Normalize()))));
        FileStream identityLock = await StateFileLock.AcquireAsync(path + "." + key + ".lock", cancellationToken).ConfigureAwait(false);
        StateSession session;
        try
        {
            await using FileStream documentLock = await StateFileLock.AcquireAsync(path + ".lock", cancellationToken).ConfigureAwait(false);
            StateDocument document = await ReadOrCreateAsync(path, cancellationToken).ConfigureAwait(false);
            session = new StateSession(path, key, document.Counters.GetValueOrDefault(key), identityLock);
        }
        catch
        {
            await identityLock.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return session;
    }

    /// <summary>Charge un document strictement validé ou crée uniquement un fichier absent.</summary>
    /// <param name="path">Fichier protégé par le verrou du document.</param>
    /// <param name="cancellationToken">Annulation de lecture ou création.</param>
    /// <returns>Document versionné, avec compteurs non négatifs.</returns>
    /// <exception cref="InvalidDataException">Version ou compteurs invalides.</exception>
    /// <exception cref="JsonException">État corrompu ou schéma invalide.</exception>
    private static async Task<StateDocument> ReadOrCreateAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            StateDocument document = JsonSerializer.Deserialize<StateDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Invalid HookRunner state document.");
            if (document.Version != 1 || document.Counters.Any(entry => entry.Value < 0 || string.IsNullOrWhiteSpace(entry.Key)))
            {
                throw new InvalidDataException("Invalid HookRunner state document.");
            }

            return document;
        }
        catch (FileNotFoundException)
        {
            StateDocument document = new(1, []);
            await WriteAsync(path, document, cancellationToken).ConfigureAwait(false);
            return document;
        }
    }

    /// <summary>Écrit, synchronise puis renomme un temporaire adjacent pour publier le document entier.</summary>
    /// <param name="path">Destination protégée par le verrou du document.</param>
    /// <param name="document">État fusionné à publier.</param>
    /// <param name="cancellationToken">Annulation observée avant le renommage atomique.</param>
    /// <returns>Une tâche terminée après publication et nettoyage du temporaire.</returns>
    /// <remarks>Une erreur d'écriture remonte sans remplacer l'ancien document.</remarks>
    private static async Task WriteAsync(string path, StateDocument document, CancellationToken cancellationToken)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Schéma interne du fichier d'état ; les clés représentent les identités normalisées.</summary>
    /// <param name="Version">Version de schéma supportée.</param>
    /// <param name="Counters">Compteurs de toutes les identités.</param>
    private sealed record StateDocument(int Version, Dictionary<string, int> Counters);

    /// <summary>Possède le verrou d'identité et fusionne chaque écriture avec le dernier document.</summary>
    /// <param name="path">Fichier partagé contenant tous les compteurs.</param>
    /// <param name="key">Clé de l'identité possédée.</param>
    /// <param name="count">Compteur initial lu sous verrou.</param>
    /// <param name="identityLock">Handle conservé pendant toute l'invocation.</param>
    private sealed class StateSession(string path, string key, int count, FileStream identityLock) : IHookStateSession
    {
        public int Count { get; private set; } = count;

        /// <inheritdoc />
        public async Task SaveAsync(int count, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            await using FileStream documentLock = await StateFileLock.AcquireAsync(path + ".lock", cancellationToken).ConfigureAwait(false);
            StateDocument document = await ReadOrCreateAsync(path, cancellationToken).ConfigureAwait(false);
            document.Counters[key] = count;
            await WriteAsync(path, document, cancellationToken).ConfigureAwait(false);
            Count = count;
        }

        /// <summary>Libère le verrou d'identité, même après une panne d'exécution ou de stockage.</summary>
        /// <returns>Une tâche de libération du handle.</returns>
        public ValueTask DisposeAsync() => identityLock.DisposeAsync();
    }
}
