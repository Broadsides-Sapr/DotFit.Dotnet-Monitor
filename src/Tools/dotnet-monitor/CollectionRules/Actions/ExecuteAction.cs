﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.WebApi;
using Microsoft.Diagnostics.Tools.Monitor.CollectionRules.Exceptions;
using Microsoft.Diagnostics.Tools.Monitor.CollectionRules.Options.Actions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor.CollectionRules.Actions
{
    internal sealed class ExecuteAction : ICollectionRuleAction<ExecuteOptions>
    {
        public async Task<CollectionRuleActionResult> ExecuteAsync(ExecuteOptions options, IEndpointInfo endpointInfo, CancellationToken cancellationToken)
        {
            string path = options.Path;
            string arguments = options.Arguments;
            bool IgnoreExitCode = options.IgnoreExitCode.GetValueOrDefault(ExecuteOptionsDefaults.IgnoreExitCode);

            ValidateFilePath(path);

            // May want to capture stdout and stderr and return as part of the result in the future
            using Process process = new Process();

            process.StartInfo = new ProcessStartInfo(path, arguments);
            process.StartInfo.RedirectStandardOutput = true;

            // Completion source that is signaled when the process exits
            TaskCompletionSource<int> exitedSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler exitedHandler = (s, e) => exitedSource.TrySetResult(process.ExitCode);

            process.EnableRaisingEvents = true;
            process.Exited += exitedHandler;

            try
            {
                if (!process.Start())
                {
                    throw new CollectionRuleActionException(new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Strings.ErrorMessage_UnableToStartProcess, process.StartInfo.FileName, process.StartInfo.Arguments)));
                }

                await WaitForExitAsync(process, exitedSource.Task, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                process.Exited -= exitedHandler;
            }

            ValidateExitCode(IgnoreExitCode, process.ExitCode);

            return new CollectionRuleActionResult()
            {
                OutputValues = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "ExitCode", process.ExitCode.ToString(CultureInfo.InvariantCulture) }
                }
            };
        }

        public async Task WaitForExitAsync(Process process, Task<int> exitedTask, CancellationToken token)
        {
            if (!process.HasExited)
            {
                await exitedTask.WithCancellation(token).ConfigureAwait(false);
            }
        }

        internal static void ValidateFilePath(string path)
        {
            if (!File.Exists(path))
            {
                throw new CollectionRuleActionException(new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, Strings.ErrorMessage_FileNotFound, path)));
            }
        }

        internal static void ValidateExitCode(bool ignoreExitCode, int exitCode)
        {
            if (!ignoreExitCode && exitCode != 0)
            {
                throw new CollectionRuleActionException(new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Strings.ErrorMessage_NonzeroExitCode, exitCode.ToString(CultureInfo.InvariantCulture))));
            }
        }
    }
}