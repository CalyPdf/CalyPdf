using System;

namespace Caly.Core;

public sealed class CalyCriticalException : Exception
{
    public CalyCriticalException() : base() { }

    public CalyCriticalException(string? message) : base(message) { }

    public CalyCriticalException(string? message, Exception? innerException) : base(message, innerException) { }
}