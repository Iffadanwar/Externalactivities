using MyrConn.PetroVisorFramework.API;
using MyrConn.PetroVisorFramework.API.Contexts;
using MyrConn.PetroVisorFramework.API.Data;
using MyrConn.PetroVisorFramework.API.Entities;
using MyrConn.PetroVisorFramework.API.Hierarchies;
using MyrConn.PetroVisorFramework.API.Models;
using MyrConn.PetroVisorFramework.API.Scopes;
using MyrConn.PetroVisorFramework.API.Workflows;
using MyrConn.WorkflowActivities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyrConn.WorkflowActivities
{
    class Program
    {
        static async Task Main()
        {

            Console.WriteLine("Entering activity...");

            // get service provider
            //var serviceProvider = new PetroVisorServiceProvider(new Uri("https://identity-latest.us1.petrovisor.com"), "Indigo CV", "kgray", );
            //var serviceProvider = new PetroVisorServiceProvider(new Uri("http://localhost:8093"), "Indigo CV", "kgray", );
            var serviceProvider = new PetroVisorServiceProvider(new Uri("https://identity-latest.us1.petrovisor.com"), "Iffad Client workspace", "ianwar", "Arainbenten10*");

            // get cancellation token
            var cancelToken = new CancellationToken();

            // instanciate class
            var activity = new CreateActiveTags();

            var forecastingPeriodArg = new ActivityMappedArgument()
            {
                ArgumentName = activity.RequiredInputArguments.ElementAt(0).ArgumentName,
                ArgumentType = MappedArgumentType.WorkspaceValue,
                MappedString = "taggapSizeArgName"
            };

            /*var logging = new ActivityMappedArgument()
            {
                ArgumentName = activity.RequiredInputArguments.ElementAt(1).ArgumentName,
                ArgumentType = MappedArgumentType.StringValue,
                MappedString = "lagging"
            };*/

            var context = serviceProvider.RepositoryServices.Get<Context>().GetItemByName("All Wells Start To End Monthly");
            var eset = serviceProvider.RepositoryServices.Get<EntitySet>().GetItemByName("Test");
            var arguments = new WorkflowActivityArguments() { Contexts = new List<Context>() { context }, EntitySet = eset, ServiceProvider = serviceProvider, WorkflowName = "Test"};

            await activity.ExecuteActivityAsync("test",
                new List<ActivityMappedArgument>() { forecastingPeriodArg }, null, arguments, cancelToken);

            Console.WriteLine("Done...");
            Console.ReadKey();
        }
    }
}
