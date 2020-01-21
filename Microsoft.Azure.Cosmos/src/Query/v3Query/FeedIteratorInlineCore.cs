﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Json;

    internal class FeedIteratorInlineCore : FeedIteratorInternal
    {
        private readonly FeedIteratorInternal feedIteratorInternal;

        internal FeedIteratorInlineCore(
            FeedIterator feedIterator)
        {
            if (feedIterator is FeedIteratorInternal feedIteratorInternal)
            {
                this.feedIteratorInternal = feedIteratorInternal;
            }
            else
            {
                throw new ArgumentNullException(nameof(feedIterator));
            }
        }

        internal FeedIteratorInlineCore(
            FeedIteratorInternal feedIteratorInternal)
        {
            this.feedIteratorInternal = feedIteratorInternal ?? throw new ArgumentNullException(nameof(feedIteratorInternal));
        }

        public override bool HasMoreResults => this.feedIteratorInternal.HasMoreResults;

        public override Task<ResponseMessage> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            return TaskHelper.RunInlineIfNeededAsync(() => this.feedIteratorInternal.ReadNextAsync(cancellationToken));
        }

        public override void SerializeState(IJsonWriter jsonWriter)
        {
            this.feedIteratorInternal.SerializeState(jsonWriter);
        }

        public override bool TryGetContinuationToken(out string continuationToken)
        {
            return this.feedIteratorInternal.TryGetContinuationToken(out continuationToken);
        }
    }

    internal class FeedIteratorInlineCore<T> : FeedIteratorInternal<T>
    {
        private readonly FeedIteratorInternal<T> feedIteratorInternal;

        internal FeedIteratorInlineCore(
            FeedIterator<T> feedIterator)
        {
            if (feedIterator is FeedIteratorInternal<T> feedIteratorInternal)
            {
                this.feedIteratorInternal = feedIteratorInternal;
            }
            else
            {
                throw new ArgumentNullException(nameof(feedIterator));
            }
        }

        internal FeedIteratorInlineCore(
            FeedIteratorInternal<T> feedIteratorInternal)
        {
            this.feedIteratorInternal = feedIteratorInternal ?? throw new ArgumentNullException(nameof(feedIteratorInternal));
        }

        public override bool HasMoreResults => this.feedIteratorInternal.HasMoreResults;

        public override Task<FeedResponse<T>> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            return TaskHelper.RunInlineIfNeededAsync(() => this.feedIteratorInternal.ReadNextAsync(cancellationToken));
        }

        public override bool TryGetContinuationToken(out string continuationToken)
        {
            return this.feedIteratorInternal.TryGetContinuationToken(out continuationToken);
        }

        public override void SerializeState(IJsonWriter jsonWriter)
        {
            this.feedIteratorInternal.SerializeState(jsonWriter);
        }
    }
}
