using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TicketSalesApp.Services.Interfaces
{
    public interface ISpacetimeDBService
    {
        /// <summary>
        /// Establishes a connection to the SpacetimeDB module
        /// </summary>
        /// <returns>The database connection</returns>
        DbConnection Connect();
        
        /// <summary>
        /// Gets the current database connection
        /// </summary>
        /// <returns>The current database connection</returns>
        DbConnection GetConnection();
        
        /// <summary>
        /// Gets the local identity for the connection
        /// </summary>
        /// <returns>The local identity</returns>
        Identity? GetLocalIdentity();
        
        /// <summary>
        /// Disconnects from the SpacetimeDB module
        /// </summary>
        void Disconnect();
        
        /// <summary>
        /// Enqueues a command to be processed by the message processing thread
        /// </summary>
        /// <param name="command">The command to enqueue</param>
        /// <param name="args">The arguments for the command</param>
        void EnqueueCommand(string command, Dictionary<string, object> args);
        
        /// <summary>
        /// Starts the message processing thread
        /// </summary>
        void StartMessageProcessing();
        
        /// <summary>
        /// Stops the message processing thread
        /// </summary>
        void StopMessageProcessing();
        
        /// <summary>
        /// Processes a single frame tick for the connection
        /// </summary>
        void ProcessFrameTick();
        
        /// <summary>
        /// Subscribes to all tables in the database
        /// </summary>
        void SubscribeToAllTables();
        
        /// <summary>
        /// Subscribes to specific queries
        /// </summary>
        /// <param name="queries">The SQL queries to subscribe to</param>
        /// <returns>The subscription handle</returns>
        SubscriptionHandle SubscribeToQueries(string[] queries);
    }
} 