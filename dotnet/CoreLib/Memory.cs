﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticMemory.Configuration;
using Microsoft.SemanticMemory.Diagnostics;
using Microsoft.SemanticMemory.Pipeline;
using Microsoft.SemanticMemory.Search;
using Microsoft.SemanticMemory.WebService;

// ReSharper disable once CheckNamespace
namespace Microsoft.SemanticMemory;

/// <summary>
/// Memory client to upload files and search for answers, without depending
/// on a web service. By design this class is hardcoded to use
/// <see cref="InProcessPipelineOrchestrator"/>, hence the name "Serverless".
/// The class accesses directly storage, vectors and AI.
/// </summary>
public class Memory : ISemanticMemoryClient
{
    private readonly SearchClient _searchClient;
    private readonly InProcessPipelineOrchestrator _orchestrator;

    public InProcessPipelineOrchestrator Orchestrator => this._orchestrator;

    public Memory(
        InProcessPipelineOrchestrator orchestrator,
        SearchClient searchClient)
    {
        this._orchestrator = orchestrator ?? throw new ConfigurationException("The orchestrator is NULL");
        this._searchClient = searchClient ?? throw new ConfigurationException("The search client is NULL");
    }

    /// <summary>
    /// Register a pipeline handler. If a handler for the same step name already exists, it gets replaced.
    /// </summary>
    /// <param name="handler">Handler instance</param>
    public void AddHandler(IPipelineStepHandler handler)
    {
        this._orchestrator.AddHandler(handler);
    }

    /// <inheritdoc />
    public async Task<string> ImportDocumentAsync(
        Document document,
        string? index = null,
        IEnumerable<string>? steps = null,
        CancellationToken cancellationToken = default)
    {
        DocumentUploadRequest uploadRequest = await document.ToDocumentUploadRequestAsync(index, steps, cancellationToken).ConfigureAwait(false);
        return await this.ImportDocumentAsync(uploadRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ImportDocumentAsync(
        string filePath,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        CancellationToken cancellationToken = default)
    {
        var document = new Document(documentId, tags: tags).AddFile(filePath);
        var uploadRequest = await document.ToDocumentUploadRequestAsync(index, steps, cancellationToken).ConfigureAwait(false);
        return await this.ImportDocumentAsync(uploadRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ImportDocumentAsync(
        DocumentUploadRequest uploadRequest,
        CancellationToken cancellationToken = default)
    {
        var index = IndexExtensions.CleanName(uploadRequest.Index);
        return await this._orchestrator.ImportDocumentAsync(index, uploadRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ImportDocumentAsync(
        Stream content,
        string? fileName = null,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        CancellationToken cancellationToken = default)
    {
        var document = new Document(documentId, tags: tags).AddStream(fileName, content);
        var uploadRequest = await document.ToDocumentUploadRequestAsync(index, steps, cancellationToken).ConfigureAwait(false);
        return await this.ImportDocumentAsync(uploadRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ImportTextAsync(
        string text,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        CancellationToken cancellationToken = default)
    {
        var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await using (content.ConfigureAwait(false))
        {
            return await this.ImportDocumentAsync(content, fileName: "content.txt", documentId: documentId, tags: tags, index: index, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string> ImportWebPageAsync(
        string url,
        string? documentId = null,
        TagCollection? tags = null,
        string? index = null,
        IEnumerable<string>? steps = null,
        CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        Verify.ValidateUrl(uri.AbsoluteUri, requireHttps: false, allowReservedIp: false, allowQuery: true);

        using Stream content = new MemoryStream(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return await this.ImportDocumentAsync(content, fileName: "content.url", documentId: documentId, tags: tags, index: index, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteDocumentAsync(string documentId, string? index = null, CancellationToken cancellationToken = default)
    {
        return this._orchestrator.StartDocumentDeletionAsync(documentId: documentId, index: index, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsDocumentReadyAsync(
        string documentId,
        string? index = null,
        CancellationToken cancellationToken = default)
    {
        index = IndexExtensions.CleanName(index);
        return await this._orchestrator.IsDocumentReadyAsync(index: index, documentId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DataPipelineStatus?> GetDocumentStatusAsync(
        string documentId,
        string? index = null,
        CancellationToken cancellationToken = default)
    {
        index = IndexExtensions.CleanName(index);
        DataPipeline? pipeline = await this._orchestrator.ReadPipelineStatusAsync(index: index, documentId, cancellationToken).ConfigureAwait(false);
        return pipeline?.ToDataPipelineStatus();
    }

    /// <inheritdoc />
    public Task<SearchResult> SearchAsync(
        string query,
        MemoryFilter? filter,
        CancellationToken cancellationToken = default)
    {
        return this.SearchAsync(query, index: null, filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SearchResult> SearchAsync(
        string query,
        string? index = null,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        index = IndexExtensions.CleanName(index);
        return this._searchClient.SearchAsync(index: index, query, filter, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MemoryAnswer> AskAsync(
        string question,
        MemoryFilter? filter,
        CancellationToken cancellationToken = default)
    {
        return this.AskAsync(question: question, index: null, filter: filter, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<MemoryAnswer> AskAsync(
        string question,
        string? index = null,
        MemoryFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        index = IndexExtensions.CleanName(index);
        return this._searchClient.AskAsync(index: index, question: question, filter: filter, cancellationToken: cancellationToken);
    }
}
