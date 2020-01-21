﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent.SkipTake
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Query.Core.ContinuationTokens;
    using Microsoft.Azure.Cosmos.Query.Core.Exceptions;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;

    internal abstract partial class SkipDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        private sealed class ComputeSkipDocumentQueryExecutionComponent : SkipDocumentQueryExecutionComponent
        {
            private const string SkipCountPropertyName = "SkipCount";
            private const string SourceTokenPropertyName = "SourceToken";

            public ComputeSkipDocumentQueryExecutionComponent(IDocumentQueryExecutionComponent source, long skipCount)
                : base(source, skipCount)
            {
                // Work is done in base constructor.
            }

            public static async Task<TryCatch<IDocumentQueryExecutionComponent>> TryCreateComputeAsync(
                int offsetCount,
                RequestContinuationToken continuationToken,
                Func<RequestContinuationToken, Task<TryCatch<IDocumentQueryExecutionComponent>>> tryCreateSourceAsync)
            {
                if (tryCreateSourceAsync == null)
                {
                    throw new ArgumentNullException(nameof(tryCreateSourceAsync));
                }

                if (!(continuationToken is CosmosElementRequestContinuationToken cosmosElementRequestContinuationToken))
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                        new ArgumentException($"Expected {nameof(RequestContinuationToken)} to be a {nameof(StringRequestContinuationToken)}"));
                }

                OffsetContinuationToken offsetContinuationToken;
                if (continuationToken != null)
                {
                    (bool parsed, OffsetContinuationToken parsedToken) = OffsetContinuationToken.TryParse(cosmosElementRequestContinuationToken.Value);
                    if (!parsed)
                    {
                        return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                            new MalformedContinuationTokenException($"Invalid {nameof(SkipDocumentQueryExecutionComponent)}: {continuationToken}."));
                    }

                    offsetContinuationToken = parsedToken;
                }
                else
                {
                    offsetContinuationToken = new OffsetContinuationToken(offsetCount, null);
                }

                if (offsetContinuationToken.Offset > offsetCount)
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                            new MalformedContinuationTokenException("offset count in continuation token can not be greater than the offsetcount in the query."));
                }

                return (await tryCreateSourceAsync(RequestContinuationToken.Create(offsetContinuationToken.SourceToken)))
                    .Try<IDocumentQueryExecutionComponent>((source) => new ClientSkipDocumentQueryExecutionComponent(
                    source,
                    offsetContinuationToken.Offset));
            }

            public override void SerializeState(IJsonWriter jsonWriter)
            {
                if (jsonWriter == null)
                {
                    throw new ArgumentNullException(nameof(jsonWriter));
                }

                if (!this.IsDone)
                {
                    jsonWriter.WriteObjectStart();
                    jsonWriter.WriteFieldName(SourceTokenPropertyName);
                    this.Source.SerializeState(jsonWriter);
                    jsonWriter.WriteFieldName(SkipCountPropertyName);
                    jsonWriter.WriteInt64Value(this.skipCount);
                    jsonWriter.WriteObjectEnd();
                }
            }

            /// <summary>
            /// A OffsetContinuationToken is a composition of a source continuation token and how many items to skip from that source.
            /// </summary>
            private readonly struct OffsetContinuationToken
            {
                /// <summary>
                /// Initializes a new instance of the OffsetContinuationToken struct.
                /// </summary>
                /// <param name="offset">The number of items to skip in the query.</param>
                /// <param name="sourceToken">The continuation token for the source component of the query.</param>
                public OffsetContinuationToken(long offset, CosmosElement sourceToken)
                {
                    if ((offset < 0) || (offset > int.MaxValue))
                    {
                        throw new ArgumentOutOfRangeException(nameof(offset));
                    }

                    this.Offset = (int)offset;
                    this.SourceToken = sourceToken;
                }

                /// <summary>
                /// The number of items to skip in the query.
                /// </summary>
                public int Offset
                {
                    get;
                }

                /// <summary>
                /// Gets the continuation token for the source component of the query.
                /// </summary>
                public CosmosElement SourceToken
                {
                    get;
                }

                public static (bool parsed, OffsetContinuationToken offsetContinuationToken) TryParse(CosmosElement value)
                {
                    if (value == null)
                    {
                        return (false, default);
                    }

                    if (!(value is CosmosObject cosmosObject))
                    {
                        return (false, default);
                    }

                    if (!cosmosObject.TryGetValue(SkipCountPropertyName, out CosmosNumber offset))
                    {
                        return (false, default);
                    }

                    if (!cosmosObject.TryGetValue(SourceTokenPropertyName, out CosmosElement sourceToken))
                    {
                        return (false, default);
                    }

                    return (true, new OffsetContinuationToken(offset.AsInteger().Value, sourceToken));
                }
            }
        }
    }
}