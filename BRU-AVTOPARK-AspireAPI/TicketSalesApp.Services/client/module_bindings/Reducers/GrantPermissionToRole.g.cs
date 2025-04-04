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
        public delegate void GrantPermissionToRoleHandler(ReducerEventContext ctx, uint roleId, uint permissionId);
        public event GrantPermissionToRoleHandler? OnGrantPermissionToRole;

        public void GrantPermissionToRole(uint roleId, uint permissionId)
        {
            conn.InternalCallReducer(new Reducer.GrantPermissionToRole(roleId, permissionId), this.SetCallReducerFlags.GrantPermissionToRoleFlags);
        }

        public bool InvokeGrantPermissionToRole(ReducerEventContext ctx, Reducer.GrantPermissionToRole args)
        {
            if (OnGrantPermissionToRole == null) return false;
            OnGrantPermissionToRole(
                ctx,
                args.RoleId,
                args.PermissionId
            );
            return true;
        }
    }

    public abstract partial class Reducer
    {
        [SpacetimeDB.Type]
        [DataContract]
        public sealed partial class GrantPermissionToRole : Reducer, IReducerArgs
        {
            [DataMember(Name = "roleId")]
            public uint RoleId;
            [DataMember(Name = "permissionId")]
            public uint PermissionId;

            public GrantPermissionToRole(
                uint RoleId,
                uint PermissionId
            )
            {
                this.RoleId = RoleId;
                this.PermissionId = PermissionId;
            }

            public GrantPermissionToRole()
            {
            }

            string IReducerArgs.ReducerName => "GrantPermissionToRole";
        }
    }

    public sealed partial class SetReducerFlags
    {
        internal CallReducerFlags GrantPermissionToRoleFlags;
        public void GrantPermissionToRole(CallReducerFlags flags) => GrantPermissionToRoleFlags = flags;
    }
}
