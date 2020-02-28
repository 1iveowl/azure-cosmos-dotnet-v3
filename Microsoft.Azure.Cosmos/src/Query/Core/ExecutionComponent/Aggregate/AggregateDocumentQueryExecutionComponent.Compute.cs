﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent.Aggregate
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Json;
    using Microsoft.Azure.Cosmos.Query.Core.ContinuationTokens;
    using Microsoft.Azure.Cosmos.Query.Core.Exceptions;
    using Microsoft.Azure.Cosmos.Query.Core.ExecutionComponent.Aggregate.Aggregators;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Query.Core.QueryClient;

    internal abstract partial class AggregateDocumentQueryExecutionComponent : DocumentQueryExecutionComponentBase
    {
        private sealed class ComputeAggregateDocumentQueryExecutionComponent : AggregateDocumentQueryExecutionComponent
        {
            private const string SourceTokenName = "SourceToken";
            private const string AggregationTokenName = "AggregationToken";

            private static readonly IReadOnlyList<CosmosElement> EmptyResults = new List<CosmosElement>().AsReadOnly();

            private ComputeAggregateDocumentQueryExecutionComponent(
                IDocumentQueryExecutionComponent source,
                SingleGroupAggregator singleGroupAggregator,
                bool isValueAggregateQuery)
                : base(source, singleGroupAggregator, isValueAggregateQuery)
            {
                // all the work is done in the base constructor.
            }

            public static async Task<TryCatch<IDocumentQueryExecutionComponent>> TryCreateAsync(
                AggregateOperator[] aggregates,
                IReadOnlyDictionary<string, AggregateOperator?> aliasToAggregateType,
                IReadOnlyList<string> orderedAliases,
                bool hasSelectValue,
                CosmosElement requestContinuation,
                Func<CosmosElement, Task<TryCatch<IDocumentQueryExecutionComponent>>> tryCreateSourceAsync)
            {
                AggregateContinuationToken aggregateContinuationToken;
                if (requestContinuation != null)
                {
                    if (!AggregateContinuationToken.TryCreateFromCosmosElement(requestContinuation, out aggregateContinuationToken))
                    {
                        return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                            new MalformedContinuationTokenException($"Malfomed {nameof(AggregateContinuationToken)}: '{requestContinuation}'"));
                    }
                }
                else
                {
                    aggregateContinuationToken = new AggregateContinuationToken(null, null);
                }

                TryCatch<SingleGroupAggregator> tryCreateSingleGroupAggregator = SingleGroupAggregator.TryCreate(
                    aggregates,
                    aliasToAggregateType,
                    orderedAliases,
                    hasSelectValue,
                    aggregateContinuationToken.SingleGroupAggregatorContinuationToken);

                if (!tryCreateSingleGroupAggregator.Succeeded)
                {
                    return TryCatch<IDocumentQueryExecutionComponent>.FromException(
                        tryCreateSingleGroupAggregator.Exception);
                }

                return (await tryCreateSourceAsync(aggregateContinuationToken.SourceContinuationToken))
                    .Try<IDocumentQueryExecutionComponent>((source) =>
                    {
                        return new ComputeAggregateDocumentQueryExecutionComponent(
                            source,
                            tryCreateSingleGroupAggregator.Result,
                            hasSelectValue);
                    });
            }

            public override async Task<QueryResponseCore> DrainAsync(
                int maxElements,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Draining aggregates is broken down into two stages
                QueryResponseCore response;
                if (!this.Source.IsDone)
                {
                    // Stage 1:
                    // Drain the aggregates fully from all continuations and all partitions
                    // And return empty pages in the meantime.
                    QueryResponseCore sourceResponse = await this.Source.DrainAsync(int.MaxValue, cancellationToken);
                    if (!sourceResponse.IsSuccess)
                    {
                        return sourceResponse;
                    }

                    foreach (CosmosElement element in sourceResponse.CosmosElements)
                    {
                        RewrittenAggregateProjections rewrittenAggregateProjections = new RewrittenAggregateProjections(
                            this.isValueAggregateQuery,
                            element);
                        this.singleGroupAggregator.AddValues(rewrittenAggregateProjections.Payload);
                    }

                    if (this.Source.IsDone)
                    {
                        response = this.GetFinalResponse();
                    }
                    else
                    {
                        response = this.GetEmptyPage(sourceResponse);
                    }
                }
                else
                {
                    // Stage 2:
                    // Return the final page after draining.
                    response = this.GetFinalResponse();
                }

                return response;
            }

            private QueryResponseCore GetFinalResponse()
            {
                List<CosmosElement> finalResult = new List<CosmosElement>();
                CosmosElement aggregationResult = this.singleGroupAggregator.GetResult();
                if (aggregationResult != null)
                {
                    finalResult.Add(aggregationResult);
                }

                QueryResponseCore response = QueryResponseCore.CreateSuccess(
                    result: finalResult,
                    requestCharge: 0,
                    activityId: null,
                    responseLengthBytes: 0,
                    disallowContinuationTokenMessage: null,
                    continuationToken: null,
                    diagnostics: QueryResponseCore.EmptyDiagnostics);

                return response;
            }

            private QueryResponseCore GetEmptyPage(QueryResponseCore sourceResponse)
            {
                // We need to give empty pages until the results are fully drained.
                QueryResponseCore response = QueryResponseCore.CreateSuccess(
                    result: EmptyResults,
                    requestCharge: sourceResponse.RequestCharge,
                    activityId: sourceResponse.ActivityId,
                    responseLengthBytes: sourceResponse.ResponseLengthBytes,
                    disallowContinuationTokenMessage: null,
                    continuationToken: sourceResponse.ContinuationToken,
                    diagnostics: sourceResponse.Diagnostics);

                return response;
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
                    jsonWriter.WriteFieldName(ComputeAggregateDocumentQueryExecutionComponent.SourceTokenName);
                    this.Source.SerializeState(jsonWriter);
                    jsonWriter.WriteFieldName(ComputeAggregateDocumentQueryExecutionComponent.AggregationTokenName);
                    this.singleGroupAggregator.SerializeState(jsonWriter);
                    jsonWriter.WriteObjectEnd();
                }
            }

            private readonly struct AggregateContinuationToken
            {
                public AggregateContinuationToken(
                    CosmosElement singleGroupAggregatorContinuationToken,
                    CosmosElement sourceContinuationToken)
                {
                    this.SingleGroupAggregatorContinuationToken = singleGroupAggregatorContinuationToken;
                    this.SourceContinuationToken = sourceContinuationToken;
                }

                public CosmosElement SingleGroupAggregatorContinuationToken { get; }

                public CosmosElement SourceContinuationToken { get; }

                public static bool TryCreateFromCosmosElement(
                    CosmosElement continuationToken,
                    out AggregateContinuationToken aggregateContinuationToken)
                {
                    if (continuationToken == null)
                    {
                        throw new ArgumentNullException(nameof(continuationToken));
                    }

                    if (!(continuationToken is CosmosObject rawAggregateContinuationToken))
                    {
                        aggregateContinuationToken = default;
                        return false;
                    }

                    if (!rawAggregateContinuationToken.TryGetValue(
                        ComputeAggregateDocumentQueryExecutionComponent.AggregationTokenName,
                        out CosmosElement singleGroupAggregatorContinuationToken))
                    {
                        aggregateContinuationToken = default;
                        return false;
                    }

                    if (!rawAggregateContinuationToken.TryGetValue(
                        ComputeAggregateDocumentQueryExecutionComponent.SourceTokenName,
                        out CosmosElement sourceContinuationToken))
                    {
                        aggregateContinuationToken = default;
                        return false;
                    }

                    aggregateContinuationToken = new AggregateContinuationToken(
                        singleGroupAggregatorContinuationToken: singleGroupAggregatorContinuationToken,
                        sourceContinuationToken: sourceContinuationToken);
                    return true;
                }
            }
        }
    }
}
