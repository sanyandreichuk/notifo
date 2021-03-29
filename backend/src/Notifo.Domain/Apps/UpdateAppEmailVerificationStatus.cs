﻿// ==========================================================================
//  Notifo.io
// ==========================================================================
//  Copyright (c) Sebastian Stehle
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Notifo.Domain.Channels.Email;
using Notifo.Infrastructure;

namespace Notifo.Domain.Apps
{
    public sealed class UpdateAppEmailVerificationStatus : ICommand<App>
    {
        public EmailVerificationStatus Status { get; set; }

        public Task<bool> ExecuteAsync(App app, IServiceProvider serviceProvider, CancellationToken ct)
        {
            if (app.EmailVerificationStatus != Status)
            {
                app.EmailVerificationStatus = Status;

                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
    }
}
