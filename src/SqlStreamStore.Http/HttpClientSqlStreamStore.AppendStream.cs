namespace SqlStreamStore
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SqlStreamStore.Imports.Ensure.That;
    using SqlStreamStore.Internal.HoneyBearHalClient;
    using SqlStreamStore.Internal.HoneyBearHalClient.Models;
    using SqlStreamStore.Streams;

    partial class HttpClientSqlStreamStore
    {
        public async Task<AppendResult> AppendToStream(
            StreamId streamId,
            int expectedVersion,
            NewStreamMessage[] messages,
            CancellationToken cancellationToken = default,
            bool useMaxCount = true)
        {
            Ensure.That(expectedVersion, nameof(expectedVersion)).IsGte(ExpectedVersion.NoStream);
            Ensure.That(messages, nameof(messages)).IsNotNull();

            GuardAgainstDisposed();

            var client = CreateClient(new Resource
            {
                Links =
                {
                    new Link
                    {
                        Href = LinkFormatter.Stream(streamId),
                        Rel = Constants.Relations.AppendToStream
                    }
                }
            });

            client = await client.Post(
                Constants.Relations.AppendToStream,
                messages,
                null,
                null,
                new Dictionary<string, string[]>
                {
                    [Constants.Headers.ExpectedVersion] = new[] { $"{expectedVersion}" }
                },
                cancellationToken);

            ThrowOnError(client);

            var resource = client.Current.First();

            return resource.Data<HalAppendResult>();
        }
    }
}