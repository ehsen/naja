# Simple Python script for .NET Compilation
# No complex libraries, just core logic

def run_demo():
    print("--- Starting Python Logic in .NET ---")

    # A simple list of names
    users = ["Alice", "Bob", "Charlie", "Diana"]
    
    # A loop with some conditional logic
    for name in users:
        name_length = len(name)
        
        if name_length > 5:
            print("Processing: " + name + " (Long Name)")
        else:
            print("Processing: " + name + " (Short Name)")

    # Simple math loop
    print("\nCalculating Squares:")
    for i in range(1, 6):
        square = i * i
        print("The square of " + str(i) + " is " + str(square))

    print("\n--- Logic Complete ---")

# Run the function
run_demo()