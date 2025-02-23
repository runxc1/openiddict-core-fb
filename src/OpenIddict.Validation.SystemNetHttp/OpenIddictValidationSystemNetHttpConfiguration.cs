﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Polly;

namespace OpenIddict.Validation.SystemNetHttp;

/// <summary>
/// Contains the methods required to ensure that the OpenIddict validation/System.Net.Http integration configuration is valid.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class OpenIddictValidationSystemNetHttpConfiguration : IConfigureOptions<OpenIddictValidationOptions>,
                                                                     IConfigureNamedOptions<HttpClientFactoryOptions>
{
    private readonly IServiceProvider _provider;
    
    /// <summary>
    /// Creates a new instance of the <see cref="OpenIddictValidationSystemNetHttpConfiguration"/> class.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    public OpenIddictValidationSystemNetHttpConfiguration(IServiceProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc/>
    public void Configure(OpenIddictValidationOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        // Register the built-in event handlers used by the OpenIddict System.Net.Http validation components.
        options.Handlers.AddRange(OpenIddictValidationSystemNetHttpHandlers.DefaultHandlers);
    }

    /// <inheritdoc/>
    public void Configure(HttpClientFactoryOptions options) => Configure(Options.DefaultName, options);

    /// <inheritdoc/>
    public void Configure(string? name, HttpClientFactoryOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        // Only amend the HTTP client factory options if the instance is managed by OpenIddict.
        var assembly = typeof(OpenIddictValidationSystemNetHttpOptions).Assembly.GetName();
        if (!string.Equals(name, assembly.Name, StringComparison.Ordinal))
        {
            return;
        }

        var settings = _provider.GetRequiredService<IOptionsMonitor<OpenIddictValidationSystemNetHttpOptions>>().CurrentValue;

        options.HttpClientActions.Add(client =>
        {
            // By default, HttpClient uses a default timeout of 100 seconds and allows payloads of up to 2GB.
            // To help reduce the effects of malicious responses (e.g responses returned at a very slow pace
            // or containing an infine amount of data), the default values are amended to use lower values.
            client.MaxResponseContentBufferSize = 10 * 1024 * 1024;
            client.Timeout = TimeSpan.FromMinutes(1);
        });

        // Register the user-defined HTTP client actions.
        foreach (var action in settings.HttpClientActions)
        {
            options.HttpClientActions.Add(action);
        }

        options.HttpMessageHandlerBuilderActions.Add(builder =>
        {
#if SUPPORTS_SERVICE_PROVIDER_IN_HTTP_MESSAGE_HANDLER_BUILDER
            var options = builder.Services.GetRequiredService<IOptionsMonitor<OpenIddictValidationSystemNetHttpOptions>>();
#else
            var options = _provider.GetRequiredService<IOptionsMonitor<OpenIddictValidationSystemNetHttpOptions>>();
#endif
            if (builder.PrimaryHandler is not HttpClientHandler handler)
            {
                throw new InvalidOperationException(SR.FormatID0373(typeof(HttpClientHandler).FullName));
            }

            // OpenIddict uses IHttpClientFactory to manage the creation of the HTTP clients and
            // their underlying HTTP message handlers, that are cached for the specified duration
            // and re-used to process multiple requests during that period. While remote APIs are
            // typically not expected to return cookies, it is in practice a very frequent case,
            // which poses a serious security issue when the cookies are shared across multiple
            // requests (which is the case when the same message handler is cached and re-used).
            //
            // To avoid that, cookies support is explicitly disabled here, for security reasons.
            handler.UseCookies = false;

            // Unless the HTTP error policy was explicitly disabled in the options,
            // add the HTTP handler responsible for replaying failed HTTP requests.
            if (options.CurrentValue.HttpErrorPolicy is IAsyncPolicy<HttpResponseMessage> policy)
            {
                builder.AdditionalHandlers.Add(new PolicyHttpMessageHandler(policy));
            }
        });

        // Register the user-defined HTTP client handler actions.
        foreach (var action in settings.HttpClientHandlerActions)
        {
            options.HttpMessageHandlerBuilderActions.Add(builder => action(builder.PrimaryHandler as HttpClientHandler ??
                throw new InvalidOperationException(SR.FormatID0373(typeof(HttpClientHandler).FullName))));
        }
    }
}
