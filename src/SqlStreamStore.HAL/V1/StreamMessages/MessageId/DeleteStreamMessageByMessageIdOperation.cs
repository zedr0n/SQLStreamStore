namespace SqlStreamStore.V1.StreamMessages.MessageId
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using SqlStreamStore.V1;

    internal class DeleteStreamMessageByMessageIdOperation : IStreamStoreOperation<Unit>
    {
        public DeleteStreamMessageByMessageIdOperation(HttpContext context)
        {
            Path = context.Request.Path;

            StreamId = context.GetRouteData().GetStreamId();
            MessageId = context.GetRouteData().GetMessageId();
        }

        public string StreamId { get; }
        public Guid MessageId { get; }
        public PathString Path { get; }

        public async Task<Unit> Invoke(IStreamStore streamStore, CancellationToken ct)
        {
            await streamStore.DeleteMessage(StreamId, MessageId, ct);

            return Unit.Instance;
        }
    }
}