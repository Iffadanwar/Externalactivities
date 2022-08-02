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
        //defining input
        const string taggapSizeArgName = "Number of days to skip for missing production.";

        //creating a workspace value
        public IEnumerable<ArgumentInfo> RequiredInputArguments => new List<ArgumentInfo>() {
        new()
            {
                ArgumentName = taggapSizeArgName,
                DefaultArgumentType = MappedArgumentType.WorkspaceValue,
                AcceptableArgumentTypes = new List<MappedArgumentType>() { MappedArgumentType.WorkspaceValue }
            }
        };


        public async Task ExecuteActivityAsync(IPetroVisorServiceProvider serviceProvider, string workflowName, string activityName, Scope overrideScope, EntitySet overrideEntitySet, IEnumerable<Context> contexts, IEnumerable<ActivityMappedArgument> mappedInputArguments, IEnumerable<ActivityMappedArgument> mappedOutputArguments, CancellationToken cancellationToken)
            {
                //getting thats value as a numeric and making sure its not null
                var minTagSkip = (int)(await GetWorkspaceValueArgumentValueAsync(serviceProvider, mappedInputArguments, taggapSizeArgName, SettingValueType.Numeric, cancellationToken))?.NumericValue;
                if (minTagSkip <= 0)
                    throw new ArgumentException($"The required argument '{taggapSizeArgName}' has an invalid value.");

                var tagentries = (await serviceProvider.TagEntriesService.GetTagEntriesAsync(new TagEntriesFilter
                {
                    Tags = new List<string> { "Active"}
                })).ToList();

                //checking for context.
                if (!contexts.Any() || contexts == null && overrideEntitySet == null && overrideScope == null)
                    {
                        await LogAsync(serviceProvider, workflowName, activityName, $"No context was specified! Please specify atleast one context", cancellationToken, severity: LogMessageSeverity.Error);
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

                    var filteredTagEntries = tagentries.Where(te => context.EntitySet.Entities.Select(x => x.Name).Contains(te.EntityName));

                    if (tagentries.Any())
                    {
                        await LogAsync(serviceProvider, workflowName, activityName, $"The required argument has Value, Please run the DeleteTagEntities workflow.", cancellationToken, severity: LogMessageSeverity.Error);
                        throw new ArgumentException($"There were active tags already assigned to some entities in the entity set. Run the deletetags activity first");
                    }

                    //loading in the production data table
                    var scriptResults = RunScript(serviceProvider, "Is well active based on production", context.Scope, context.EntitySet, new NamedParametersValues());

                        //creating a timespan of 3 days to avoide creating multiple active tags
                        TimeSpan offlineTime = new TimeSpan(minTagSkip, 0, 0, 0);

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
                            for (var i = 0; i < activeSteps.Length; i++)
                            {
                                //checks for last instance
                                if ((i + 1) == activeSteps.Length)
                                {
                                    var startTime = activeSteps[holdNum].Date;
                                    var endTime = activeSteps[counter].Date;
                                    var tag = new TagEntry() { Start = startTime, End = endTime, Entity = ent };
                                    timeForActiveTagsList.Add(tag);
                                }
                                //if the next date is less than offlineTime : counter++
                                else if (activeSteps[i+1].Date.Subtract(activeSteps[i].Date) < offlineTime)
                                {
                                    counter++;
                                }
                                //if the next date is more than offlineTime : add to taglist, counter++, holdnum = counter
                                else if (activeSteps[i+1].Date.Subtract(activeSteps[i].Date) >= offlineTime)
                                {

                                    var startTime = activeSteps[holdNum].Date;
                                    var endTime = activeSteps[counter].Date;
                                    var tag = new TagEntry() { Start = startTime, End = endTime, Entity = ent };
                                    timeForActiveTagsList.Add(tag);

                                    counter++;

                                    holdNum = counter;

                                }
                                else
                                {
                                    await LogAsync(serviceProvider, workflowName, activityName, $"Error executing the script: Time data need to be in datetime format", cancellationToken, severity: LogMessageSeverity.Error);
                                    throw new Exception($"Error executing the script: Time data need to be in datetime format");
                                }

                            }

                        }

                        // creating tags

                        await serviceProvider.TagEntriesService.AddOrEditTagEntriesAsync(timeForActiveTagsList, cancellationToken);
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
        }
    }
}

