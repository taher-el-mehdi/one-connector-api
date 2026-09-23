using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using DocuWareSageConnector.Infrastructure.Configuration;

namespace DocuWareSageConnector.Infrastructure.Synchronization;

public interface IResilienceExecutor
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);

    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}

public sealed class ResilienceExecutor : IResilienceExecutor
{
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<ResilienceExecutor> _logger;

    public ResilienceExecutor(
        IOptions<SynchronizationOptions> options,
        ILogger<ResilienceExecutor> logger)
    {
        _logger = logger;
        var settings = options.Value;
        var firstDelay = TimeSpan.FromSeconds(Math.Max(settings.FirstRetryDelaySeconds, 0));

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = args =>
                    ValueTask.FromResult(
                        args.Outcome.Exception is not null
                        && ErrorClassifier.IsTransient(args.Outcome.Exception)),
                MaxRetryAttempts = Math.Max(settings.MaxRetries, 0),
                BackoffType = DelayBackoffType.Exponential,
                Delay = firstDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : firstDelay,
                DelayGenerator = args =>
                {
                    if (args.Outcome.Exception is not null
                        && ErrorClassifier.TryGetRetryAfter(args.Outcome.Exception, out var retryAfter)
                        && retryAfter > TimeSpan.Zero)
                    {
                        return ValueTask.FromResult<TimeSpan?>(retryAfter);
                    }

                    return ValueTask.FromResult<TimeSpan?>(null);
                },
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Transient failure. Retry {Attempt} after {Delay}.",
                        args.AttemptNumber,
                        args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
        await _pipeline.ExecuteAsync(async ct => await action(ct).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        await ExecuteAsync(async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken)
            .ConfigureAwait(false);
}
