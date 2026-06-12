using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HeatherAmiDigital.FlowInterrogator.Core.Tests.Fakes;

/// <summary>
/// An <see cref="HttpMessageHandler"/> test double that records requests and returns a
/// pre-seeded queue of responses, enabling <see cref="System.Net.Http.HttpClient"/>-based
/// services to be tested without a network.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;

    /// <summary>Gets the request URIs seen by this handler, in order.</summary>
    public List<string> RequestUris { get; } = new();

    /// <summary>
    /// Initializes a new instance seeded with the responses returned, in order, by successive sends.
    /// </summary>
    public FakeHttpMessageHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses ?? Array.Empty<HttpResponseMessage>());
    }

    /// <summary>Creates a JSON success response with the supplied body.</summary>
    public static HttpResponseMessage Json(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri?.ToString());
        return Task.FromResult(_responses.Dequeue());
    }
}

/// <summary>A <see cref="ITokenProvider"/> stub returning a constant token.</summary>
public sealed class FakeTokenProvider : HeatherAmiDigital.FlowInterrogator.Core.Services.ITokenProvider
{
    private readonly string _token;

    /// <summary>Initializes a new instance returning the supplied token.</summary>
    public FakeTokenProvider(string token = "fake-token") => _token = token;

    /// <inheritdoc />
    public Task<string> GetAccessTokenAsync() => Task.FromResult(_token);
}
