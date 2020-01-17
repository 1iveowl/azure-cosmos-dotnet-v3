﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent.SkipTake
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Query.Core.ContinuationTokens;
    using Microsoft.Azure.Cosmos.Query.Core.Exceptions;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Newtonsoft.Json;

    internal abstract partial class SkipDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        private sealed class ClientSkipDocumentQueryExecutionComponent : SkipDocumentQueryExecutionComponent
        {
            public ClientSkipDocumentQueryExecutionComponent(IDocumentQueryExecutionComponent source, long skipCount)
                : base(source, skipCount)
            {
                // Work is done in base constructor.
            }

            public static async Task<TryCatch<IDocumentQueryExecutionComponent>> TryCreateAsync(
                int offsetCount,
                RequestContinuationToken continuationToken,
                Func<RequestContinuationToken, Task<TryCatch<IDocumentQueryExecutionComponent>>> tryCreateSourceAsync)
            {
                if (tryCreateSourceAsync == null)
                {
                    throw new ArgumentNullException(nameof(tryCreateSourceAsync));
                }

                if (!(continuationToken is StringRequestContinuationToken stringRequestContinuationToken))
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                        new ArgumentException($"Expected {nameof(RequestContinuationToken)} to be a {nameof(StringRequestContinuationToken)}"));
                }

                OffsetContinuationToken offsetContinuationToken;
                if (continuationToken != null)
                {
                    if (!OffsetContinuationToken.TryParse(stringRequestContinuationToken.Value, out offsetContinuationToken))
                    {
                        return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                            new MalformedContinuationTokenException($"Invalid {nameof(SkipDocumentQueryExecutionComponent)}: {continuationToken}."));
                    }
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
                throw new NotImplementedException();
            }

            public override bool TryGetContinuationToken(out string state)
            {
                throw new NotImplementedException();
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
                public OffsetContinuationToken(int offset, string sourceToken)
                {
                    if (offset < 0)
                    {
                        throw new ArgumentException($"{nameof(offset)} must be a non negative number.");
                    }

                    this.Offset = offset;
                    this.SourceToken = sourceToken;
                }

                /// <summary>
                /// The number of items to skip in the query.
                /// </summary>
                [JsonProperty("offset")]
                public int Offset
                {
                    get;
                }

                /// <summary>
                /// Gets the continuation token for the source component of the query.
                /// </summary>
                [JsonProperty("sourceToken")]
                public string SourceToken
                {
                    get;
                }

                /// <summary>
                /// Tries to parse out the OffsetContinuationToken.
                /// </summary>
                /// <param name="value">The value to parse from.</param>
                /// <param name="offsetContinuationToken">The result of parsing out the token.</param>
                /// <returns>Whether or not the LimitContinuationToken was successfully parsed out.</returns>
                public static bool TryParse(string value, out OffsetContinuationToken offsetContinuationToken)
                {
                    offsetContinuationToken = default;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    try
                    {
                        offsetContinuationToken = JsonConvert.DeserializeObject<OffsetContinuationToken>(value);
                        return true;
                    }
                    catch (JsonException)
                    {
                        return false;
                    }
                }

                /// <summary>
                /// Gets the string version of the continuation token that can be passed in a response header.
                /// </summary>
                /// <returns>The string version of the continuation token that can be passed in a response header.</returns>
                public override string ToString()
                {
                    return JsonConvert.SerializeObject(this);
                }
            }
        }
    }
}