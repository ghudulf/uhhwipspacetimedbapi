// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteReducers : RemoteBase
    {
        public delegate void ActivateRouteHandler(ReducerEventContext ctx, uint routeId, SpacetimeDB.Identity? actingUser);
        public event ActivateRouteHandler? OnActivateRoute;

        public void ActivateRoute(uint routeId, SpacetimeDB.Identity? actingUser)
        {
            conn.InternalCallReducer(new Reducer.ActivateRoute(routeId, actingUser), this.SetCallReducerFlags.ActivateRouteFlags);
        }

        public bool InvokeActivateRoute(ReducerEventContext ctx, Reducer.ActivateRoute args)
        {
            if (OnActivateRoute == null) return false;
            OnActivateRoute(
                ctx,
                args.RouteId,
                args.ActingUser
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class ActivateRoute : Reducer, IReducerArgs
        {
            [DataMember(Name = "routeId")]
            public uint RouteId;
            [DataMember(Name = "actingUser")]
            public SpacetimeDB.Identity? ActingUser;

            public ActivateRoute(
                uint RouteId,
                SpacetimeDB.Identity? ActingUser
            )
            {
                this.RouteId = RouteId;
                this.ActingUser = ActingUser;
            }

            public ActivateRoute()
            {
            }

            string IReducerArgs.ReducerName => "ActivateRoute";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags ActivateRouteFlags;
        public void ActivateRoute(CallReducerFlags flags) => ActivateRouteFlags = flags;
    }
}
