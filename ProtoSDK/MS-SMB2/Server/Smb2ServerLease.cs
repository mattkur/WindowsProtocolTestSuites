﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Modeling;

namespace Microsoft.Protocols.TestTools.StackSdk.FileAccessService.Smb2
{
    public class Smb2ServerLease
    {
        public SequenceContainer<byte> LeaseKey;

        public string FileName;

        public LeaseStateValues LeaseState;

        public LeaseStateValues BreakToLeaseState;

        // LeaseBreakTimeout

        public Set<Smb2ServerOpen> LeaseOpens;

        public bool Breaking;
    }
}
