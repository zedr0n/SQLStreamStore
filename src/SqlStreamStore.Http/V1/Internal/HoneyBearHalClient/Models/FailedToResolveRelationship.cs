namespace SqlStreamStore.V1.Internal.HoneyBearHalClient.Models
{
    using System;

    internal sealed class FailedToResolveRelationship : Exception
    {
        public FailedToResolveRelationship(string relationship)
            : base($"Failed to resolve relationship:{relationship}")
        {

        }
    }
}