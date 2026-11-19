namespace HookRunner.Application;

public interface IRequest;

public interface IResponse;

public interface IUseCase<in TRequest, TResponse>
    where TRequest : IRequest
    where TResponse : IResponse
{
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken);
}
