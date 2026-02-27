using System.Text;
using SpacetimeDB;

public static partial class Module
{ // ***** Employee Management *****
    [SpacetimeDB.Table(Public = true)]
    public partial class Employee
    {
        [PrimaryKey]
        public uint EmployeeId;          // Auto-incremented
        public string Surname;
        public string Name;
        public string? Patronym;         // Optional
        public ulong EmployedSince;      // Unix timestamp
        public uint JobId;               // References Job.JobId
        public string? BadgeNumber;      // Employee badge/ID number
        public string? ContactPhone;     // Contact phone number
        public string? ContactEmail;     // Contact email
        public ulong? DateOfBirth;       // Unix timestamp
        public string? PassportNumber;   // Passport number
        public string? PassportIssuedBy; // Passport issuing authority
        public ulong? PassportIssuedDate; // Unix timestamp
        public string? PhotoUrl;         // URL to employee photo
        public string? Address;          // Home address
        public string? EmergencyContact; // Emergency contact information
        public ulong? LastTrainingDate;  // Date of last training/instruction
        public string? TrainingStatus;   // "Completed", "Pending", "Expired"
        public string? CurrentStatus;    // "Available", "On Route", "On Break", "Off Duty", "Sick Leave"
        public string[]? Certifications; // Certifications held by the employee
        public ulong? CertificationExpiry; // When certifications expire
        public string? MedicalCertificate; // Medical certificate information
        public ulong? MedicalCertificateExpiry; // When medical certificate expires
        public string? DriverLicenseNumber; // Driver's license number
        public string? DriverLicenseCategory; // Driver's license category
        public ulong? DriverLicenseExpiry; // When driver's license expires
        public uint? YearsOfExperience;  // Years of experience
        public string[]? LanguagesSpoken; // Languages spoken by the employee
        public string? PreferredShiftType; // Preferred shift type
        public string[]? SkillsAndQualifications; // Skills and qualifications
        public string? PerformanceRating; // Performance rating
        public uint? VacationDaysRemaining; // Vacation days remaining
        public uint? SickDaysUsed;       // Sick days used
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class Job
    {
        [PrimaryKey]
        public uint JobId;               // Auto-incremented
        public string JobTitle;
        public string? Internship;       // String, since it can have varied text
        public double? BaseSalary;       // Base salary for the position
        public string? Department;       // Department (e.g., "Operations", "Maintenance")
        public string? JobDescription;   // Description of job responsibilities
        public uint? RequiredExperience; // Required experience in years
        public string[]? RequiredSkills; // Required skills for the job
        public string[]? RequiredCertifications; // Required certifications
        public string? EducationRequirements; // Education requirements
        public string? WorkSchedule;     // Work schedule description
        public bool? IsFullTime;         // Whether the job is full-time
        public bool? IsPartTime;         // Whether the job is part-time
        public bool? IsShiftWork;        // Whether the job involves shift work
        public string[]? Benefits;       // Benefits offered with the job
        public string? ReportingTo;      // Job title this position reports to
        public uint? VacationDays;       // Vacation days per year
        public uint? SickDays;           // Sick days per year
        public string? PerformanceMetrics; // Performance metrics for the job
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class EmployeeShift
    {
        [PrimaryKey]
        public uint ShiftId;             // Auto-incremented
        public uint EmployeeId;          // References Employee.EmployeeId
        public ulong ShiftStartDate;     // Start date of the shift
        public ulong ShiftEndDate;       // End date of the shift
        public ulong ShiftStartTime;     // Start time of the shift
        public ulong ShiftEndTime;       // End time of the shift
        public string ShiftType;         // "Day", "Evening", "Night"
        public string ShiftStatus;       // "Scheduled", "Completed", "Day Off", "Sick Leave"
        public uint? RouteId;            // References Route.RouteId (optional)
        public string? Notes;            // Additional notes about the shift
        public Identity? AssignedBy;     // Who assigned this shift
        public ulong AssignedAt;         // When the shift was assigned
        public string? ShiftForm;        // "Строенная", "Двухсполовинная", "Сдвоенная"
        public double ShiftDuration;     // Duration in hours
        public ulong? BreakStartTime;    // Start time of the break
        public ulong? BreakEndTime;      // End time of the break
        public double? BreakDuration;    // Duration of the break in hours
        public bool? IsOvertime;         // Whether this is an overtime shift
        public double? OvertimeHours;    // Number of overtime hours
        public string? ReplacementFor;   // Employee this person is replacing
        public bool? IsTraining;         // Whether this is a training shift
        public string? TrainerName;      // Name of the trainer
        public string? ShiftLocation;    // Location of the shift
        public string? ShiftFeedback;    // Feedback about the shift
        public bool? ComplianceWithRegulations; // Whether the shift complies with regulations
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class CashierDay
    {
        [PrimaryKey]
        public uint CashierDayId;        // Auto-incremented
        public double Revenue;           // Total revenue collected
        public uint TicketsSold;         // Number of tickets sold
        public uint FinesCollected;      // Number of fines collected
        public double FinesAmount;       // Total amount from fines
        public ulong Date;               // Date of the cashier day
        public uint EmployeeId;          // References Employee.EmployeeId (conductor)
        public uint RouteId;             // References Route.RouteId
        public string? Notes;            // Additional notes
        public bool Reconciled;          // Whether the cash has been reconciled
        public Identity? ReconciledBy;   // Who reconciled the cash
        public ulong? ReconciledAt;      // When the cash was reconciled
        public double? CashCollected;    // Amount of cash collected
        public double? ElectronicPayments; // Amount of electronic payments
        public uint? ElectronicTransactions; // Number of electronic transactions
        public uint? CashTransactions;   // Number of cash transactions
        public double? Discrepancy;      // Discrepancy between expected and actual cash
        public string? DiscrepancyReason; // Reason for any discrepancy
        public bool? IsBalanced;         // Whether the cash is balanced
        public string? ShiftType;        // "Morning", "Afternoon", "Evening"
        public uint? PassengerCount;     // Number of passengers served
        public double? AverageTicketPrice; // Average ticket price
    }
    
    
    // This table is for tracking conductor performance on routes, and also for route conductor management.
    [SpacetimeDB.Table(Public = true)]
    public partial class RouteConductor
    {
        [PrimaryKey]
        public uint RouteConductorId;
        public uint RouteId;
        public uint EmployeeId;         // Employee id of the conductor

        // Shift types in Belarus public transport system:
        // 1. Day shift: 06:00-14:00 with 1 hour break (usually 10:00-11:00)
        // 2. Evening shift: 14:00-22:00 with 1 hour break (usually 17:00-18:00)
        // 3. Triple shift system: First 07:00-15:30, Second 15:30-24:00, with 30 min breaks between shifts
        // 4. Two-and-half shift system: First 06:30-13:30, Second 13:30-20:30, with 30 min breaks between shifts

        public string ShiftName;        // Tracks shift name for the conductor (Day, Evening, Night, etc.)
        public string ShiftType;        // Regular, Triple, Two-and-half

        public ulong ShiftStart;        // Tracks shift start time
        public ulong ShiftEnd;          // Tracks shift end time
        public ulong BreakStart;        // When the conductor's break starts
        public ulong BreakEnd;          // When the conductor's break ends
        public ulong ShiftDuration;     // Tracks total shift duration

        // Ticket and passenger management
        public long SaleCountNumber;    // Number of tickets sold during shift
        public double TotalSalesAmount; // Total monetary amount from ticket sales
        public long EvictionCount;      // Number of passengers evicted for non-payment
        public long ApproximateNumberOfPassengers; // Estimated passenger count during shift

        // Safety and incident tracking
        public long Incidents;          // Count of incidents on the route
        public long SafetyViolationsReported; // Safety violations reported by conductor
        public long PassengerAssistanceCount; // Number of times conductor assisted passengers
        public long RouteInformationProvidedCount; // Number of times conductor provided route information

        // Performance metrics
        public uint PerformanceRating;  // Overall performance rating
        public bool FirstAidProvided;   // Whether conductor provided first aid during shift
        public bool EmergencyResponseRequired; // Whether emergency response was needed
        public double FareEvasionPercentage; // Percentage of passengers without tickets
        public double TotalFinesAmount; // Total amount collected from fines
        public long ComplaintCount;     // Number of complaints received

        // Electronic systems usage
        public bool ElectronicValidationUsed; // Whether electronic validation systems were used
        public long ElectronicPaymentsCount; // Number of electronic payments processed
        public long CashPaymentsCount;  // Number of cash payments processed

        // Administrative data
        public ulong Timestamp;         // When this record was created
        public string? Notes;           // Additional notes about the shift
        public bool ShiftHandoverCompleted; // Whether shift handover was properly completed
        public double CashCollected;    // Amount of cash collected during shift
        public string? HandoverNotes;   // Notes about the handover to next conductor
        public string? CurrentLocation; // Current location of the conductor
        public string? EmployeeStatus;  // "Available", "On Route", "On Break"
    }

    [SpacetimeDB.Table(Public = true)]
    public partial class ConductorStatistics
    {
        [PrimaryKey]
        public uint StatId;             // Auto-incremented
        public uint EmployeeId;         // References Employee.EmployeeId (conductor)
        public ulong PeriodStart;       // Start of the statistical period
        public ulong PeriodEnd;         // End of the statistical period
        public long TotalPassengersServed; // Total number of passengers served
        public double AverageFareEvasionRate; // Average fare evasion rate
        public double TotalRevenueCollected; // Total revenue collected
        public double TotalFinesCollected; // Total fines collected
        public long TotalComplaintsReceived; // Total complaints received
        public double AverageRating;    // Average performance rating
        public long TotalShiftsWorked;  // Total number of shifts worked
        public double AveragePassengersPerShift; // Average passengers per shift
        public double EfficiencyScore;  // Overall efficiency score
        public long TotalTicketsSold;   // Total number of tickets sold
        public double AverageTicketsPerHour; // Average tickets sold per hour
        public double AverageRevenuePerShift; // Average revenue per shift
        public long TotalIncidentsReported; // Total incidents reported
        public long TotalSafetyViolationsReported; // Total safety violations reported
        public long TotalPassengerAssistance; // Total passenger assistance provided
        public double AveragePerformanceRating; // Average performance rating
        public long TotalFirstAidProvided; // Total first aid provided
        public long TotalEmergencyResponses; // Total emergency responses
    }
}