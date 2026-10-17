using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Tools;
using RimMind.Application.Common.Models.Tools;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Tools
{
    /// <summary>
    /// Introspection tool allowing AI Agents to dynamically query tool definitions,
    /// parameter specifications, schemas, and capabilities at runtime.
    /// </summary>
    public sealed class QueryToolHelpTool : IToolHandler
    {
        private readonly IToolRegistry _toolRegistry;

        public QueryToolHelpTool(IToolRegistry toolRegistry)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        }

        public string Id => "query_tool_help";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;

        public ToolDefinition Definition { get; } = new ToolDefinition
        {
            Id = "query_tool_help",
            Category = "meta",
            Description = "Query parameter descriptions, schemas, and usage guidelines for registered AI tools. Can query a specific tool or list all available tools.",
            ParametersSchema = "{\"type\":\"object\",\"properties\":{\"tool_name\":{\"type\":\"string\",\"description\":\"Name or Id of the tool to inspect (e.g. 'actions_place_blueprint', 'query_buildable_structures'). If omitted, returns all available tool IDs and brief summaries.\"}},\"required\":[]}"
        };

        public Task<Result<ToolResult, RimMindError>> ExecuteAsync(ToolCallArgs args, CancellationToken ct)
        {
            string? toolName = ExtractToolName(args.ArgumentsJson);

            if (!string.IsNullOrWhiteSpace(toolName))
            {
                var handler = _toolRegistry.FindById(toolName!);
                if (handler == null)
                {
                    var allIds = _toolRegistry.All.Select(t => t.Definition.Id).OrderBy(id => id).ToList();
                    var notFoundPayload = new
                    {
                        status = "not_found",
                        message = $"Tool '{toolName}' is not registered.",
                        available_tools = allIds
                    };
                    return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
                    {
                        ToolCallId = args.ToolCallId,
                        ToolName = Id,
                        Content = JsonConvert.SerializeObject(notFoundPayload),
                        IsError = true
                    }));
                }

                var def = handler.Definition;
                var toolHelpPayload = new
                {
                    status = "ok",
                    tool_id = def.Id,
                    category = def.Category,
                    description = def.Description,
                    parameters_schema = ParseSchemaOrRaw(def.ParametersSchema)
                };

                return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
                {
                    ToolCallId = args.ToolCallId,
                    ToolName = Id,
                    Content = JsonConvert.SerializeObject(toolHelpPayload),
                    IsError = false
                }));
            }

            var allTools = _toolRegistry.All
                .Select(t => new
                {
                    id = t.Definition.Id,
                    category = t.Definition.Category,
                    description = t.Definition.Description
                })
                .OrderBy(t => t.category)
                .ThenBy(t => t.id)
                .ToList();

            var catalogPayload = new
            {
                status = "ok",
                total_tools = allTools.Count,
                tools = allTools
            };

            return Task.FromResult(Result<ToolResult, RimMindError>.Ok(new ToolResult
            {
                ToolCallId = args.ToolCallId,
                ToolName = Id,
                Content = JsonConvert.SerializeObject(catalogPayload),
                IsError = false
            }));
        }

        private static string? ExtractToolName(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var token = JObject.Parse(json!);
                return token["tool_name"]?.Value<string>()
                    ?? token["tool"]?.Value<string>()
                    ?? token["name"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        private static object ParseSchemaOrRaw(string schema)
        {
            try
            {
                return JToken.Parse(schema);
            }
            catch
            {
                return schema;
            }
        }
    }
}
