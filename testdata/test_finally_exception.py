hit_else = False
hit_finally = False
hit_except = False
hit_inner_except = False
hit_inner_else = False
hit_inner_finally = False

try:
    try:
        pass
    except:
        hit_inner_except = True
    else:
        hit_inner_else = True
    finally:
        hit_inner_finally = True
        raise Exception('outer exception')
except:
    hit_except = True
else:
    hit_else = True
finally:
    hit_finally = True

print(f'hit_inner_except={hit_inner_except}')
print(f'hit_inner_else={hit_inner_else}')
print(f'hit_inner_finally={hit_inner_finally}')
print(f'hit_except={hit_except}')
print(f'hit_else={hit_else}')
print(f'hit_finally={hit_finally}')
