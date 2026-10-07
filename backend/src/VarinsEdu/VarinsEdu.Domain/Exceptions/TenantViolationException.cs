namespace VarinsEdu.Domain.Exceptions;

// Thrown when code tries to write data that does not belong to the current institution.
public class TenantViolationException(string message) : Exception(message);
