using System.Collections.Generic;
using System.Threading.Tasks;

using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.RDS;
using Amazon.RDS.Model;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

using NUnit.Framework;

namespace Mutedac.WaitForDatabaseAvailability
{
    class WaitForDatabaseAvailabilityTests : TestSuite<WaitForDatabaseAvailabilityTests.Context>
    {
        private const string waitForDatabaseAvailabilityRuleName = "waitForDatabaseAvailabilityRuleName";
        private const string dequeueEventSourceUuid = "dequeueUuid";

        internal class Context : IContext
        {

#pragma warning disable CS8618, CS0649

            [Substitute] IAmazonRDS RdsClient;
            [Substitute] IAmazonLambda LambdaClient;
            [Substitute] IAmazonEventBridge EventBridgeClient;
            public WaitForDatabaseAvailabilityHandler StartDatabaseHandler;

#pragma warning restore CS8618, CS0649

            public Task Setup()
            {
                var logger = Substitute.For<ILogger<WaitForDatabaseAvailabilityHandler>>();
                var configuration = new OptionsWrapper<LambdaConfiguration>(new LambdaConfiguration
                {
                    WaitForDatabaseAvailabilityRuleName = waitForDatabaseAvailabilityRuleName,
                    DequeueEventSourceUUID = dequeueEventSourceUuid,
                });

                StartDatabaseHandler = new WaitForDatabaseAvailabilityHandler(RdsClient, LambdaClient, EventBridgeClient, logger, configuration);
                return Task.CompletedTask;
            }

            public void Deconstruct(
                out IAmazonRDS rdsClient,
                out IAmazonLambda lambdaClient,
                out IAmazonEventBridge eventsClient,
                out WaitForDatabaseAvailabilityHandler handler
            )
            {
                rdsClient = RdsClient;
                lambdaClient = LambdaClient;
                eventsClient = EventBridgeClient;
                handler = StartDatabaseHandler;
            }
        }

        [Test]
        public async Task DisableRule_IsCalled_IfDatabaseIsAvailable()
        {
            var (rdsClient, lambdaClient, eventsClient, handler) = await GetContext();
            var databaseName = "databaseName";

            rdsClient.DescribeDBClustersAsync(Arg.Any<DescribeDBClustersRequest>()).Returns(new DescribeDBClustersResponse
            {
                DBClusters = new List<DBCluster>
                {
                    new DBCluster
                    {
                        DBClusterIdentifier = databaseName,
                        Status = "available"
                    }
                }
            });

            await handler.Handle(new WaitForDatabaseAvailabilityRequest
            {
                DatabaseName = databaseName
            });

            await eventsClient.Received().DisableRuleAsync(Arg.Is<DisableRuleRequest>(req => req.Name == waitForDatabaseAvailabilityRuleName));
            await rdsClient.Received().DescribeDBClustersAsync(Arg.Is<DescribeDBClustersRequest>(req => req.DBClusterIdentifier == databaseName));
        }

        [Test]
        public async Task DisableRule_IsNotCalled_IfDatabaseIsNotAvailable()
        {
            var (rdsClient, lambdaClient, eventsClient, handler) = await GetContext();
            var databaseName = "databaseName";

            rdsClient.DescribeDBClustersAsync(Arg.Any<DescribeDBClustersRequest>()).Returns(new DescribeDBClustersResponse
            {
                DBClusters = new List<DBCluster>
                {
                    new DBCluster
                    {
                        DBClusterIdentifier = databaseName,
                        Status = "starting"
                    }
                }
            });

            await handler.Handle(new WaitForDatabaseAvailabilityRequest
            {
                DatabaseName = databaseName
            });

            await eventsClient.DidNotReceiveWithAnyArgs().DisableRuleAsync(null);
            await rdsClient.Received().DescribeDBClustersAsync(Arg.Is<DescribeDBClustersRequest>(req => req.DBClusterIdentifier == databaseName));
        }

        [Test]
        public async Task LambdaEventSource_IsEnabled_IfDatabaseIsAvailable()
        {
            var (rdsClient, lambdaClient, eventsClient, handler) = await GetContext();
            var databaseName = "databaseName";

            rdsClient.DescribeDBClustersAsync(Arg.Any<DescribeDBClustersRequest>()).Returns(new DescribeDBClustersResponse
            {
                DBClusters = new List<DBCluster>
                {
                    new DBCluster
                    {
                        DBClusterIdentifier = databaseName,
                        Status = "available"
                    }
                }
            });

            await handler.Handle(new WaitForDatabaseAvailabilityRequest
            {
                DatabaseName = databaseName
            });

            await lambdaClient.Received().UpdateEventSourceMappingAsync(Arg.Is<UpdateEventSourceMappingRequest>(req =>
               req.UUID == dequeueEventSourceUuid &&
               req.Enabled == true
            ));

            await rdsClient.Received().DescribeDBClustersAsync(Arg.Is<DescribeDBClustersRequest>(req =>
                req.DBClusterIdentifier == databaseName
            ));
        }
    }
}
