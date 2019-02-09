﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Pipelines
{
    /// <summary>
    /// Defines a class that provides access to a read side of pipe.
    /// </summary>
    public abstract partial class PipeReader
    {
        /// <summary>
        /// Attempt to synchronously read data the <see cref="PipeReader"/>.
        /// </summary>
        /// <param name="result">The <see cref="ReadResult"/></param>
        /// <returns>True if data was available, or if the call was canceled or the writer was completed.</returns>
        /// <remarks>If the pipe returns false, there's no need to call <see cref="AdvanceTo(SequencePosition, SequencePosition)"/>.</remarks>
        public abstract bool TryRead(out ReadResult result);

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current <see cref="PipeReader"/>.
        /// </summary>
        /// <returns>A <see cref="ValueTask{T}"/> representing the asynchronous read operation.</returns>
        public abstract ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves forward the pipeline's read cursor to after the consumed data.
        /// </summary>
        /// <param name="consumed">Marks the extent of the data that has been successfully processed.</param>
        /// <remarks>
        /// The memory for the consumed data will be released and no longer available.
        /// The examined data communicates to the pipeline when it should signal more data is available.
        /// </remarks>
        public abstract void AdvanceTo(SequencePosition consumed);

        /// <summary>
        /// Moves forward the pipeline's read cursor to after the consumed data.
        /// </summary>
        /// <param name="consumed">Marks the extent of the data that has been successfully processed.</param>
        /// <param name="examined">Marks the extent of the data that has been read and examined.</param>
        /// <remarks>
        /// The memory for the consumed data will be released and no longer available.
        /// The examined data communicates to the pipeline when it should signal more data is available.
        /// </remarks>
        public abstract void AdvanceTo(SequencePosition consumed, SequencePosition examined);

        /// <summary>
        /// Cancel to currently pending or if none is pending next call to <see cref="ReadAsync"/>, without completing the <see cref="PipeReader"/>.
        /// </summary>
        public abstract void CancelPendingRead();

        /// <summary>
        /// Signal to the producer that the consumer is done reading.
        /// </summary>
        /// <param name="exception">Optional <see cref="Exception"/> indicating a failure that's causing the pipeline to complete.</param>
        public abstract void Complete(Exception exception = null);

        /// <summary>
        /// Cancel the pending <see cref="ReadAsync"/> operation. If there is none, cancels next <see cref="ReadAsync"/> operation, without completing the <see cref="PipeWriter"/>.
        /// </summary>
        public abstract void OnWriterCompleted(Action<Exception, object> callback, object state);

        /// <summary>
        /// Asynchronously reads the bytes from the <see cref="PipeReader"/> and writes them to the specified stream, using a specified buffer size and cancellation token.
        /// </summary>
        /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A task that represents the asynchronous copy operation.</returns>
        public virtual Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            return CopyToAsyncCore(destination, cancellationToken);
        }

        private async Task CopyToAsyncCore(Stream destination, CancellationToken cancellationToken)
        {
            while (true)
            {
                SequencePosition consumed = default;

                try
                {
                    ReadResult result = await ReadAsync(cancellationToken).ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    consumed = buffer.Start;

                    if (result.IsCanceled)
                    {
                        throw new OperationCanceledException();
                    }

                    while (buffer.TryGet(ref consumed, out ReadOnlyMemory<byte> memory))
                    {
                        await destination.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    // Advance even if WriteAsync throws so the PipeReader is not left in the
                    // currently reading state
                    AdvanceTo(consumed);
                }
            }
        }
    }
}
