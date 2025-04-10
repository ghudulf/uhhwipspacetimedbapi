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
        public sealed class TwoFactorTokenHandle : RemoteTableHandle<EventContext, TwoFactorToken>
        {
            protected override string RemoteTableName => "TwoFactorToken";

            public sealed class IdUniqueIndex : UniqueIndexBase<uint>
            {
                protected override uint GetKey(TwoFactorToken row) => row.Id;

                public IdUniqueIndex(TwoFactorTokenHandle table) : base(table) { }
            }

            public readonly IdUniqueIndex Id;

            internal TwoFactorTokenHandle(DbConnection conn) : base(conn)
            {
                Id = new(this);
            }

            protected override object GetPrimaryKey(TwoFactorToken row) => row.Id;
        }

        public readonly TwoFactorTokenHandle TwoFactorToken;
    }
}
