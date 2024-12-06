using Dorc.ApiModel.Constants;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Dorc.Monitor.RunnerProcess.StandardStreamRedirection
{
    internal class AsyncStreamReader : IDisposable
    {
        internal const int MaxByteBufferSize = 1024 * 10;

        private readonly Stream sourceStream;
        private readonly Decoder sourceStreamDecoder;
        private readonly byte[] readBytes;
        private readonly char[] readChars;

        private readonly Action<string?> messageReadCallback;

        private CancellationTokenSource? readerCancellationTokenSource;
        private Task? readToBufferTask;

        private readonly Queue<string?> messageQueue;

        private StringBuilder? messageStringBuilder;
        private bool isLastCarriageReturn;

        private int currentLinePosition;

        private AsyncStreamReader() { }

        internal AsyncStreamReader(
            Stream sourceStream,
            Action<string?> messageReadCallback,
            Encoding sourceStreamEncoding)
        {
            if (!sourceStream.CanRead)
            {
                throw new InvalidOperationException("Stream must be readable.");
            }

            this.sourceStream = sourceStream;

            this.messageReadCallback = messageReadCallback;

            sourceStreamDecoder = sourceStreamEncoding.GetDecoder();
            readBytes = new byte[MaxByteBufferSize];
            int maxCharsPerBuffer = sourceStreamEncoding.GetMaxCharCount(MaxByteBufferSize);
            readChars = new char[maxCharsPerBuffer];

            messageQueue = new Queue<string?>();
        }

        internal void BeginReadLine(CancellationToken cancellationToken)
        {
            readerCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (messageStringBuilder == null)
            {
                messageStringBuilder = new StringBuilder(MaxByteBufferSize);
                readToBufferTask = ReadBufferAsync();
            }
            else
            {
                FlushMessageQueue(false);
            }
        }

        internal void CancelOperation()
        {
            readerCancellationTokenSource?.Cancel();
        }

        private async Task ReadBufferAsync()
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            while (true)
            {
                try
                {
                    int readByteCount = await sourceStream.ReadAsync(
                        new Memory<byte>(readBytes),
                        readerCancellationTokenSource!.Token)
                        .ConfigureAwait(false);

                    if (readByteCount == 0)
                    {
                        break;
                    }

                    int readCharCount = sourceStreamDecoder.GetChars(
                        readBytes,
                        0,
                        readByteCount,
                        readChars,
                        0);

                    messageStringBuilder!.Append(
                        readChars,
                        0,
                        readCharCount);

                    bool isMessageLast = MoveLinesFromStringBuilderToMessageQueue();

                    if (isMessageLast)
                    {
                        // Treat this as EOF without processing current buffer content
                        return;
                    }
                }
                catch (IOException)
                {
                    // We should ideally consume errors from operations getting cancelled
                    // so that we don't crash the unsuspecting parent with an unhandled exc.
                    // This seems to come in 2 forms of exceptions (depending on platform and scenario),
                    // namely OperationCanceledException and IOException (for errorcode that we don't
                    // map explicitly).
                    break; // Treat this as EOF
                }
                catch (OperationCanceledException)
                {
                    // We should consume any OperationCanceledException from child read here
                    // so that we don't crash the parent with an unhandled exc
                    break; // Treat this as EOF
                }

                // If user's delegate throws exception we treat this as EOF and
                // completing without processing current buffer content
                if (FlushMessageQueue(true))
                {
                    return;
                }
            }

            // We're at EOF, process current buffer content and flush message queue.
            lock (messageQueue)
            {
                if (messageStringBuilder!.Length != 0)
                {
                    string message = messageStringBuilder.ToString();

                    messageQueue.Enqueue(message);

                    messageStringBuilder.Length = 0;
                }

                messageQueue.Enqueue(null);
            }

            FlushMessageQueue(true);
        }

        private bool MoveLinesFromStringBuilderToMessageQueue()
        {
            bool isMessageLast = false;

            int currentIndex = currentLinePosition;
            int lineStart = 0;
            int stringLength = messageStringBuilder!.Length;

            if (isLastCarriageReturn
                && stringLength > 0
                && messageStringBuilder[0] == '\n')
            {
                currentIndex = 1;
                lineStart = 1;
                isLastCarriageReturn = false;
            }

            while (currentIndex < stringLength)
            {
                char currentCharacter = messageStringBuilder[currentIndex];
                if (currentCharacter == '\r'
                    || currentCharacter == '\n')
                {
                    string lineString = messageStringBuilder.ToString(lineStart, currentIndex - lineStart);
                    lineStart = currentIndex + 1; //next line start index
                    // skip the "\n" character following "\r" character
                    if (currentCharacter == '\r'
                        && lineStart < stringLength
                        && messageStringBuilder[lineStart] == '\n')
                    {
                        lineStart++;
                        currentIndex++;
                    }

                    lock (messageQueue)
                    {
                        if (lineString.Contains(RunnerConstants.StandardStreamEndString))
                        {
                            isMessageLast = true;
                        }
                        else
                        {
                            messageQueue.Enqueue(lineString);
                        }
                    }
                }
                currentIndex++;
            }

            // Protect length as IndexOutOfRangeException was being thrown when less than a
            // character's worth of bytes was read at the beginning of a line.
            if (stringLength > 0
                && messageStringBuilder[stringLength - 1] == '\r')
            {
                isLastCarriageReturn = true;
            }

            // Keep the rest characaters which can't form a new line in string builder.
            if (lineStart < stringLength)
            {
                if (lineStart == 0)
                {
                    // we found no breaklines, in this case we cache the position
                    // so next time we don't have to restart from the beginning
                    currentLinePosition = currentIndex;
                }
                else
                {
                    messageStringBuilder.Remove(0, lineStart);
                    currentLinePosition = 0;
                }
            }
            else
            {
                messageStringBuilder.Length = 0;
                currentLinePosition = 0;
            }

            return isMessageLast;
        }

        private bool FlushMessageQueue(bool rethrowInNewThread)
        {
            try
            {
                while (true)
                {
                    string? regularMessage = null;
                    lock (messageQueue)
                    {
                        if (messageQueue.Count == 0)
                        {
                            break;
                        }

                        regularMessage = messageQueue.Dequeue();
                    }

                    if (!readerCancellationTokenSource!.IsCancellationRequested)
                    {
                        messageReadCallback(regularMessage);
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                // If rethrowInNewThread is true, we can't let the exception propagate synchronously on this thread,
                // so propagate it in a thread pool thread and return true to indicate to the caller that this failed.
                // Otherwise, let the exception propagate.
                if (rethrowInNewThread)
                {
                    ThreadPool.QueueUserWorkItem(edi => ((ExceptionDispatchInfo)edi!).Throw(), ExceptionDispatchInfo.Capture(e));
                    return true;
                }
                throw;
            }
        }

        internal Task EOF => readToBufferTask ?? Task.CompletedTask;

        public void Dispose()
        {
            readerCancellationTokenSource?.Cancel();
        }
    }
}
