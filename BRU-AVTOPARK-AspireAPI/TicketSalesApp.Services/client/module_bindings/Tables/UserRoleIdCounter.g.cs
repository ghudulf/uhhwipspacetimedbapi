// THIS FILE IS AUTOMATICALLY GENERATED BY SPACETIMEDB. EDITS TO THIS FILE
// WILL NOT BE SAVED. MODIFY TABLES IN YOUR MODULE SOURCE CODE INSTEAD.

#nullable enable

using System;
using SpacetimeDB.BSATN;
using SpacetimeDB.ClientApi;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SpacetimeDB.Types
{
    public sealed partial class RemoteTables
    {
        public sealed class UserRoleIdCounterHandle : RemoteTableHandle<EventContext, UserRoleIdCounter>
        {
            protected override string RemoteTableName => "UserRoleIdCounter";

            public sealed class KeyUniqueIndex : UniqueIndexBase<string>
            {
                protected override string GetKey(UserRoleIdCounter row) => row.Key;

                public KeyUniqueIndex(UserRoleIdCounterHandle table) : base(table) { }
            }

            public readonly KeyUniqueIndex Key;

            internal UserRoleIdCounterHandle(DbConnection conn) : base(conn)
            {
                Key = new(this);
            }

            protected override object GetPrimaryKey(UserRoleIdCounter row) => row.Key;
        }

        public readonly UserRoleIdCounterHandle UserRoleIdCounter;
    }
}
