namespace SqlStreamStore.V1
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using MySql.Data.MySqlClient;
    using SqlStreamStore.V1.Infrastructure;
    using SqlStreamStore.V1.MySqlScripts;
    using SqlStreamStore.V1.Streams;

    partial class MySqlStreamStore
    {
        protected override async Task<AppendResult> AppendToStreamInternal(
            string streamId,
            int expectedVersion,
            NewStreamMessage[] messages,
            CancellationToken cancellationToken)
        {
            var streamIdInfo = new StreamIdInfo(streamId);

            try
            {
                return messages.Length == 0
                    ? await CreateEmptyStream(streamIdInfo, expectedVersion, cancellationToken)
                    : await AppendMessagesToStream(streamIdInfo, expectedVersion, messages, cancellationToken);
            }
            catch(MySqlException ex) when(ex.IsWrongExpectedVersion())
            {
                throw new WrongExpectedVersionException(
                    ErrorMessages.AppendFailedWrongExpectedVersion(
                        streamIdInfo.MySqlStreamId.IdOriginal,
                        expectedVersion),
                    streamIdInfo.MySqlStreamId.IdOriginal,
                    expectedVersion,
                    ex);
            }
        }

        private async Task<AppendResult> AppendMessagesToStream(
            StreamIdInfo streamId,
            int expectedVersion,
            NewStreamMessage[] messages,
            CancellationToken cancellationToken)
        {
            var appendResult = new AppendResult(StreamVersion.End, Position.End);
            var nextExpectedVersion = expectedVersion;

            using(var connection = await OpenConnection(cancellationToken))
            using(var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .NotOnCapturedContext())
            {
                var throwIfAdditionalMessages = false;

                for(var i = 0; i < messages.Length; i++)
                {
                    bool messageExists;
                    (nextExpectedVersion, appendResult, messageExists) = await AppendMessageToStream(
                        streamId,
                        nextExpectedVersion,
                        messages[i],
                        transaction,
                        cancellationToken);

                    if(i == 0)
                    {
                        throwIfAdditionalMessages = messageExists;
                    }
                    else
                    {
                        if(throwIfAdditionalMessages && !messageExists)
                        {
                            throw new WrongExpectedVersionException(
                                ErrorMessages.AppendFailedWrongExpectedVersion(
                                    streamId.MySqlStreamId.IdOriginal,
                                    expectedVersion),
                                streamId.MySqlStreamId.IdOriginal,
                                expectedVersion);
                        }
                    }
                }

                await transaction.CommitAsync(cancellationToken).NotOnCapturedContext();
            }

            if(_settings.ScavengeAsynchronously)
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                Task.Run(() => TryScavenge(streamId, cancellationToken), cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
            else
            {
                await TryScavenge(streamId, cancellationToken).NotOnCapturedContext();
            }

            return appendResult;
        }

        private Task<(int nextExpectedVersion, AppendResult appendResult, bool messageExists)> AppendMessageToStream(
            StreamIdInfo streamId,
            int expectedVersion,
            NewStreamMessage message,
            MySqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            switch(expectedVersion)
            {
                case ExpectedVersion.Any:
                    return AppendToStreamExpectedVersionAny(streamId, message, transaction, cancellationToken);
                case ExpectedVersion.NoStream:
                    return AppendToStreamExpectedVersionNoStream(streamId, message, transaction, cancellationToken);
                case ExpectedVersion.EmptyStream:
                    return AppendToStreamExpectedVersionEmptyStream(streamId, message, transaction, cancellationToken);
                default:
                    return AppendToStreamExpectedVersion(
                        streamId,
                        expectedVersion,
                        message,
                        transaction,
                        cancellationToken);
            }
        }

        private async Task<(int nextExpectedVersion, AppendResult appendResult, bool messageExists)>
            AppendToStreamExpectedVersionAny(
                StreamIdInfo streamId,
                NewStreamMessage message,
                MySqlTransaction transaction,
                CancellationToken cancellationToken)
        {
            var currentVersion = Parameters.CurrentVersion();
            var currentPosition = Parameters.CurrentPosition();
            var messageExists = Parameters.MessageExists();

            using(var command = BuildStoredProcedureCall(
                _schema.AppendToStreamExpectedVersionAny,
                transaction,
                Parameters.StreamId(streamId.MySqlStreamId),
                Parameters.StreamIdOriginal(streamId.MySqlStreamId),
                Parameters.MetadataStreamId(streamId.MetadataMySqlStreamId),
                Parameters.CreatedUtc(_settings.GetUtcNow?.Invoke()),
                Parameters.MessageId(message.MessageId),
                Parameters.Type(message.Type),
                Parameters.JsonData(message.JsonData),
                Parameters.JsonMetadata(message.JsonMetadata),
                currentVersion,
                currentPosition,
                messageExists))
            {
                var nextExpectedVersion = Convert.ToInt32(
                    await command
                        .ExecuteScalarAsync(cancellationToken)
                        .NotOnCapturedContext());
                return (
                    nextExpectedVersion,
                    new AppendResult(
                        (int) currentVersion.Value,
                        (long) currentPosition.Value),
                    (bool) messageExists.Value);
            }
        }

        private async Task<(int nextExpectedVersion, AppendResult appendResult, bool messageExists)>
            AppendToStreamExpectedVersionNoStream(
                StreamIdInfo streamId,
                NewStreamMessage message,
                MySqlTransaction transaction,
                CancellationToken cancellationToken)
        {
            var currentVersion = Parameters.CurrentVersion();
            var currentPosition = Parameters.CurrentPosition();
            var messageExists = Parameters.MessageExists();

            using(var command = BuildStoredProcedureCall(
                _schema.AppendToStreamExpectedVersionNoStream,
                transaction,
                Parameters.StreamId(streamId.MySqlStreamId),
                Parameters.StreamIdOriginal(streamId.MySqlStreamId),
                Parameters.MetadataStreamId(streamId.MetadataMySqlStreamId),
                Parameters.CreatedUtc(_settings.GetUtcNow?.Invoke()),
                Parameters.MessageId(message.MessageId),
                Parameters.Type(message.Type),
                Parameters.JsonData(message.JsonData),
                Parameters.JsonMetadata(message.JsonMetadata),
                currentVersion,
                currentPosition,
                messageExists))
            {
                var nextExpectedVersion = Convert.ToInt32(
                    await command
                        .ExecuteScalarAsync(cancellationToken)
                        .NotOnCapturedContext());
                return (
                    nextExpectedVersion,
                    new AppendResult(
                        (int) currentVersion.Value,
                        (long) currentPosition.Value),
                    (bool) messageExists.Value);
            }
        }

        private async Task<(int nextExpectedVersion, AppendResult appendResult, bool messageExists)>
            AppendToStreamExpectedVersionEmptyStream(
                StreamIdInfo streamId,
                NewStreamMessage message,
                MySqlTransaction transaction,
                CancellationToken cancellationToken)
        {
            var currentVersion = Parameters.CurrentVersion();
            var currentPosition = Parameters.CurrentPosition();
            var messageExists = Parameters.MessageExists();

            using(var command = BuildStoredProcedureCall(
                _schema.AppendToStreamExpectedVersionEmptyStream,
                transaction,
                Parameters.StreamId(streamId.MySqlStreamId),
                Parameters.StreamIdOriginal(streamId.MySqlStreamId),
                Parameters.MetadataStreamId(streamId.MetadataMySqlStreamId),
                Parameters.CreatedUtc(_settings.GetUtcNow?.Invoke()),
                Parameters.MessageId(message.MessageId),
                Parameters.Type(message.Type),
                Parameters.JsonData(message.JsonData),
                Parameters.JsonMetadata(message.JsonMetadata),
                currentVersion,
                currentPosition,
                messageExists))
            {
                var nextExpectedVersion = Convert.ToInt32(
                    await command
                        .ExecuteScalarAsync(cancellationToken)
                        .NotOnCapturedContext());
                return (
                    nextExpectedVersion,
                    new AppendResult(
                        (int) currentVersion.Value,
                        (long) currentPosition.Value),
                    (bool) messageExists.Value);
            }
        }

        private async Task<(int nextExpectedVersion, AppendResult appendResult, bool messageExists)>
            AppendToStreamExpectedVersion(
                StreamIdInfo streamId,
                int expectedVersion,
                NewStreamMessage message,
                MySqlTransaction transaction,
                CancellationToken cancellationToken)
        {
            var currentVersion = Parameters.CurrentVersion();
            var currentPosition = Parameters.CurrentPosition();
            var messageExists = Parameters.MessageExists();

            using(var command = BuildStoredProcedureCall(
                _schema.AppendToStreamExpectedVersion,
                transaction,
                Parameters.StreamId(streamId.MySqlStreamId),
                Parameters.ExpectedVersion(expectedVersion),
                Parameters.CreatedUtc(_settings.GetUtcNow?.Invoke()),
                Parameters.MessageId(message.MessageId),
                Parameters.Type(message.Type),
                Parameters.JsonData(message.JsonData),
                Parameters.JsonMetadata(message.JsonMetadata),
                messageExists,
                currentVersion,
                currentPosition))
            {
                await command.PrepareAsync(cancellationToken).NotOnCapturedContext();
                var nextExpectedVersion = Convert.ToInt32(
                    await command
                        .ExecuteScalarAsync(cancellationToken)
                        .NotOnCapturedContext());
                return (
                    nextExpectedVersion,
                    new AppendResult(
                        (int) currentVersion.Value,
                        (long) currentPosition.Value),
                    (bool) messageExists.Value);
            }
        }

        private async Task<AppendResult> CreateEmptyStream(
            StreamIdInfo streamId,
            int expectedVersion,
            CancellationToken cancellationToken)
        {
            var appendResult = new AppendResult(StreamVersion.End, Position.End);

            using(var connection = await OpenConnection(cancellationToken))
            using(var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .NotOnCapturedContext())
            using(var command = BuildStoredProcedureCall(
                _schema.CreateEmptyStream,
                transaction,
                Parameters.StreamId(streamId.MySqlStreamId),
                Parameters.StreamIdOriginal(streamId.MySqlStreamId),
                Parameters.MetadataStreamId(streamId.MetadataMySqlStreamId),
                Parameters.ExpectedVersion(expectedVersion)))
            using(var reader = await command.ExecuteReaderAsync(cancellationToken).NotOnCapturedContext())
            {
                await reader.ReadAsync(cancellationToken).NotOnCapturedContext();

                appendResult = new AppendResult(reader.GetInt32(0), reader.GetInt64(1));

                reader.Close();

                await transaction.CommitAsync(cancellationToken).NotOnCapturedContext();
            }

            return appendResult;
        }
    }
}