namespace Tarui.Contracts;

/// <summary>A single named HTTP header value.</summary>
public sealed record HttpHeader(string Name, string Value);

/// <summary>
/// Request for the <c>plugin:http|fetch</c> command. <see cref="Url"/> is validated against the
/// caller capability's allow/deny URL scopes (default deny) before any request is made, and every
/// redirect hop is re-checked against the same scopes.
/// </summary>
public sealed record HttpRequestOptions(
    string Method,
    string Url,
    HttpHeader[]? Headers = null,
    string? Body = null,
    int? TimeoutMs = null,
    string? Channel = null);

/// <summary>Non-streaming fetch result: response status, headers, and the response body as text.</summary>
public sealed record HttpResponseResult(int Status, HttpHeader[] Headers, string? Body);

/// <summary>Leading metadata frame of a streamed response: status line plus headers.</summary>
public sealed record HttpStreamMeta(int Status, string? StatusText, HttpHeader[] Headers);

/// <summary>
/// A single frame streamed over a <c>TaruiChannel</c>. <c>Kind</c> is <c>"meta"</c> for the leading
/// <see cref="Meta"/> frame and <c>"chunk"</c> for each <see cref="Data"/> slice; the final success resolve
/// signals the end of the stream.
/// </summary>
public sealed record HttpStreamEvent(string Kind, HttpStreamMeta? Meta = null, byte[]? Data = null);

/// <summary>A text form field for a multipart upload.</summary>
public sealed record HttpField(string Name, string Value);

/// <summary>A binary file part for a multipart upload. <see cref="Name"/> is the form field name, <see cref="FileName"/>
/// the client filename, <see cref="Data"/> the raw bytes; <see cref="ContentType"/> defaults to octet-stream.</summary>
public sealed record HttpFilePart(string Name, string FileName, byte[] Data, string? ContentType = null);

/// <summary>
/// Request for <c>plugin:http|upload</c>: a scoped multipart/form-data POST. <see cref="Url"/> is validated
/// against the caller URL scopes (default deny) and every redirect hop is re-checked, mirroring
/// <c>plugin:http|fetch</c>.
/// </summary>
public sealed record HttpUploadOptions(
    string Url,
    HttpHeader[]? Headers = null,
    HttpField[]? Fields = null,
    HttpFilePart[]? Files = null,
    int? TimeoutMs = null);

/// <summary>Non-streaming upload result: response status, headers, and the response body as text.</summary>
public sealed record HttpUploadResult(int Status, HttpHeader[] Headers, string? Body);