// SPDX-FileCopyrightText: 2026 Younes BENMOUSSA <younes.benmoussa@pm.me>
// SPDX-License-Identifier: LGPL-3.0-or-later

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Core.Results;
using Core.Serialization.Codecs.Json;
using Core.Serialization.Documents;

namespace Core.Serialization.Mapping;

/// <summary>
/// Maps a plain object to and from a <see cref="DataDocument"/> through the JSON
/// codec, using the caller's source-generated <see cref="JsonTypeInfo{T}"/> so the
/// mapping is reflection-free and NativeAOT-safe.
/// </summary>
/// <remarks>
/// This is the typed bridge for configuration and state: a product keeps its
/// record types and its <c>JsonSerializerContext</c>, then reads and writes the
/// shared document — JSON on disk, or any other codec through the document. It
/// does not replace a hand-written adapter for a text-only format such as INI,
/// whose repeated keys need domain logic.
/// </remarks>
public static class DataDocumentMapper
{
    /// <summary>Serializes a value and reads it back as a document.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The source-generated type information; must not be <see langword="null"/>.</param>
    /// <returns>The document, or a failure from the JSON reader.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static Result<DataDocument> ToDocument<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

        return JsonDataReader.Read(bytes);
    }

    /// <summary>Serializes a document and deserializes it into a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="document">The document; must not be <see langword="null"/>.</param>
    /// <param name="typeInfo">The source-generated type information; must not be <see langword="null"/>.</param>
    /// <returns>The value, or a failure with code <c>serialization.invalid_document</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static Result<T> FromDocument<T>(DataDocument document, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var encoded = JsonDataWriter.WriteToUtf8Bytes(document);

        if (encoded.IsFailure)
        {
            return Result<T>.Failure(encoded.Error);
        }

        try
        {
            return Result<T>.Success(JsonSerializer.Deserialize(encoded.Value, typeInfo)!);
        }
        catch (JsonException exception)
        {
            return Result<T>.Failure(
                new CoreError(
                    "serialization.invalid_document",
                    $"The document does not map to {typeInfo.Type.Name}: {exception.Message}"));
        }
    }
}
