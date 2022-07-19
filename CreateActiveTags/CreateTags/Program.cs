using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MyrConn.PetroVisorFramework.API;
using MyrConn.PetroVisorFramework.API.Configuration;
using MyrConn.PetroVisorFramework.API.Contexts;
using MyrConn.PetroVisorFramework.API.Data;
using MyrConn.PetroVisorFramework.API.Entities;
using MyrConn.PetroVisorFramework.API.Logging;
using MyrConn.PetroVisorFramework.API.Parsing;
using MyrConn.PetroVisorFramework.API.Scopes;
using MyrConn.PetroVisorFramework.API.Scripts;
using MyrConn.PetroVisorFramework.API.Tags;
using MyrConn.PetroVisorFramework.API.Workflows;

namespace MyrConn.WorkflowActivities
{
    public class CreateActiveTags : AbstractCustomWorkflowActivity
    {
        const string tagArgName = "Tag Name";

        public override IEnumerable<ArgumentInfo> RequiredInputArguments => new List<ArgumentInfo>() {
            new(){ArgumentName = tagArgName, DefaultArgumentType = MappedArgumentType.TagEntries}
        };

        public override async Task ExecuteActivityAsync(IPetroVisorServiceProvider serviceProvider, string workflowName, string activityName, Scope overrideScope, EntitySet overrideEntitySet, IEnumerable<Context> contexts, IEnumerable<ActivityMappedArgument> mappedInputArguments, IEnumerable<ActivityMappedArgument> mappedOutputArguments, CancellationToken cancellationToken)
        {


            // confirm that there are no tags or it will run DeleteTagEntities workflow
            string filterstring = mappedInputArguments.FirstOrDefault(item => item.ArgumentName == tagArgName).MappedString;
            if (filterstring.Equals("Active"))
            {
                await LogAsync(serviceProvider, workflowName,activityName, $"The required argument '{tagArgName}' has Value, Please run the DeleteTagEntities workflow.", cancellationToken, severity: LogMessageSeverity.Error);
                throw new ArgumentException($"The required argument '{tagArgName}' has Value, Please run the DeleteTagEntities workflow.");
            }

            // c contexts or the ovverride entity set/ scope set

            if (!contexts.Any() || contexts == null && overrideEntitySet == null && overrideScope == null)
            {
                await LogAsync(serviceProvider, workflowName,activityName, $"No context was specified! Please specify atleast one context", cancellationToken, severity: LogMessageSeverity.Error);
                throw new ArgumentException($"No context was specified! Please specify atleast one context");
            }
            //looping throught multiple context and scopes.
            foreach (var context in contexts)
            {
                if (overrideEntitySet != null)
                {
                    context.EntitySet = overrideEntitySet;
                }
                if (overrideScope != null)
                {
                    context.Scope = overrideScope;
                }

                //loading in the production data table
                var scriptResults = RunScript(serviceProvider, "Is well active based on production", context.Scope, context.EntitySet, new NamedParametersValues());
                
                //creating a timespan of 3 days to avoide creating multiple active tags
                TimeSpan dt1 = new TimeSpan(3, 0, 0, 0);

                //creating a tagEntry list
                List<TagEntry> timeForActiveTagsList = new List<TagEntry>();

                //logic and removes all false bool values and loops throught data to accuratly create time tags.
                foreach (var result in scriptResults.First().DataBool)
                {
                    // mapping entity to use in tag creation
                    var ent = context.EntitySet.Entities.FirstOrDefault(x => x.Name == result.Entity);
                    // removing all false values
                    var activeSteps = result.Data.Where(x => x.Value).ToArray();

                    //holding values
                    var counter = 0;
                    var holdNum = 0;

                    //loop creates tags for each entity 
                    for (var i = 0; i < activeSteps.Length - 1; i++)
                    {

                        if (activeSteps[i++].Date.Subtract(activeSteps[i].Date) <= dt1)
                        {
                            counter++;
                        }
                        else if (activeSteps[i++].Date.Subtract(activeSteps[i].Date) >= dt1)
                        {

                            var startTime = activeSteps[holdNum].Date;
                            var endTime = activeSteps[counter].Date;
                            var tag = new TagEntry() { Start = startTime, End = endTime, Entity = ent };
                            timeForActiveTagsList.Add(tag);

                            counter++;

                            holdNum = counter;

                            Console.WriteLine(startTime.ToString());
                            Console.WriteLine(endTime.ToString());

                        }
                        else
                        {
                            await LogAsync(serviceProvider, workflowName, activityName, $"Error executing the script: Time data need to be in datetime format", cancellationToken, severity: LogMessageSeverity.Error);
                            throw new Exception($"Error executing the script: Time data need to be in datetime format");
                        }

                    }

                }

                // creating tags
                var creatingTags = serviceProvider.TagEntriesService.AddOrEditTagEntriesAsync(timeForActiveTagsList, cancellationToken);
            };


        }

        private static List<ResultDataTable> RunScript(IPetroVisorServiceProvider serviceProvider, string scriptName, Scope scope, EntitySet eset, NamedParametersValues parameters)
        {
            // run script to get data from PV
            try
            {
                var executionOptions = new ScriptExecutionOptions
                {
                    TreatScriptContentAsScriptName = true,
                    NamedParametersWithValues = parameters,
                    OverrideScope = scope,
                    OverrideEntitySet = eset
                };

                var scriptResult = serviceProvider.PSharpService.Execute(new ScriptWithExecutionOptions
                {
                    ScriptContent = scriptName,
                    Options = executionOptions
                }).ToList();

                return scriptResult;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error executing the script. " + ExceptionSupport.GetFullExceptionMessage(ex));
            }
        }

        private static async Task LogAsync(IPetroVisorServiceProvider sp, string workflowName, string activityName, string message, CancellationToken cancellationToken, Exception ex = null, LogMessageSeverity severity = LogMessageSeverity.Warning, bool logToApp = true)
        {
            if (ex != null)
                message += Environment.NewLine + ExceptionSupport.GetFullExceptionMessage(ex, true);
            if (logToApp)
            {
                LogEntry le = new()
                {
                    Message = message,
                    Signal = null,
                    Script = activityName,
                    Severity = severity,
                    Category = ConfigurationSettings.WorkflowExecutionLogName,
                    Workflow = workflowName
                };
                await sp.LoggingService.AddLogEntryAsync(le, cancellationToken);
            }

            Console.WriteLine(message);
        }
    }
}

