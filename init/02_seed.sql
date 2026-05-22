BEGIN;

INSERT INTO regions (region_id, region_name) VALUES
    (1, 'Europe'),
    (2, 'Americas'),
    (3, 'Asia'),
    (4, 'Middle East and Africa');

INSERT INTO countries (country_id, country_name, region_id) VALUES
    ('UK', 'United Kingdom', 1),
    ('DE', 'Germany', 1),
    ('US', 'United States of America', 2),
    ('CA', 'Canada', 2),
    ('JP', 'Japan', 3),
    ('IN', 'India', 3);

INSERT INTO locations (location_id, street_address, postal_code, city, state_province, country_id) VALUES
    (1400, '2014 Jabberwocky Rd', '26192', 'Southlake', 'Texas', 'US'),
    (1500, '2011 Interiors Blvd', '99236', 'South San Francisco', 'California', 'US'),
    (1700, '2004 Charade Rd', '98199', 'Seattle', 'Washington', 'US'),
    (1800, '147 Spadina Ave', 'M5V 2L7', 'Toronto', 'Ontario', 'CA'),
    (2400, '8204 Arthur St', NULL, 'London', NULL, 'UK'),
    (2500, 'Magdalen Centre, The Oxford Science Park', 'OX9 9ZB', 'Oxford', 'Oxford', 'UK'),
    (2700, 'Schwanthalerstr. 7031', '80925', 'Munich', 'Bavaria', 'DE'),
    (2900, '1297 Residency Road', '560025', 'Bengaluru', 'Karnataka', 'IN'),
    (3000, '9-7 Marunouchi', '100-0005', 'Tokyo', 'Tokyo', 'JP'),
    (3300, 'Schillerstrasse 11', '60313', 'Frankfurt', 'Hesse', 'DE');

INSERT INTO jobs (job_id, job_title, min_salary, max_salary) VALUES
    ('AD_PRES', 'President', 20000, 40000),
    ('AD_VP', 'Administration Vice President', 15000, 30000),
    ('AD_ASST', 'Administration Assistant', 3000, 6000),
    ('FI_MGR', 'Finance Manager', 8200, 16000),
    ('FI_ACCOUNT', 'Accountant', 4200, 9000),
    ('IT_PROG', 'Programmer', 4000, 10000),
    ('MK_MAN', 'Marketing Manager', 9000, 15000),
    ('SA_MAN', 'Sales Manager', 10000, 20000),
    ('SA_REP', 'Sales Representative', 6000, 12000),
    ('HR_REP', 'Human Resources Representative', 4000, 9000),
    ('MK_REP', 'Marketing Representative', 4000, 9000);

INSERT INTO departments (department_id, department_name, manager_id, location_id) VALUES
    (10, 'Administration', 200, 1700),
    (20, 'Marketing', 201, 1800),
    (40, 'Human Resources', 203, 2400),
    (60, 'IT', 103, 1400),
    (80, 'Sales', 145, 2500),
    (90, 'Executive', 100, 1700),
    (100, 'Finance', 108, 1700);

INSERT INTO employees (
    employee_id,
    first_name,
    last_name,
    email,
    phone_number,
    hire_date,
    job_id,
    salary,
    commission_pct,
    manager_id,
    department_id
) VALUES
    (100, 'Steven', 'King', 'SKING', '515.123.4567', '2003-06-17', 'AD_PRES', 24000.00, NULL, NULL, 90),
    (101, 'Neena', 'Kochhar', 'NKOCHHAR', '515.123.4568', '2005-09-21', 'AD_VP', 17000.00, NULL, 100, 90),
    (102, 'Lex', 'De Haan', 'LDEHAAN', '515.123.4569', '2001-01-13', 'AD_VP', 17000.00, NULL, 100, 90),
    (103, 'Alexander', 'Hunold', 'AHUNOLD', '590.423.4567', '2006-01-03', 'IT_PROG', 9000.00, NULL, 102, 60),
    (104, 'Bruce', 'Ernst', 'BERNST', '590.423.4568', '2007-05-21', 'IT_PROG', 6000.00, NULL, 103, 60),
    (105, 'David', 'Austin', 'DAUSTIN', '590.423.4569', '2005-06-25', 'IT_PROG', 4800.00, NULL, 103, 60),
    (106, 'Valli', 'Pataballa', 'VPATABAL', '590.423.4560', '2006-02-05', 'IT_PROG', 4800.00, NULL, 103, 60),
    (107, 'Diana', 'Lorentz', 'DLORENTZ', '590.423.5567', '2007-02-07', 'IT_PROG', 4200.00, NULL, 103, 60),
    (108, 'Nancy', 'Greenberg', 'NGREENBE', '515.124.4569', '2002-08-17', 'FI_MGR', 12000.00, NULL, 101, 100),
    (109, 'Daniel', 'Faviet', 'DFAVIET', '515.124.4169', '2002-08-16', 'FI_ACCOUNT', 9000.00, NULL, 108, 100),
    (110, 'John', 'Chen', 'JCHEN', '515.124.4269', '2005-09-28', 'FI_ACCOUNT', 8200.00, NULL, 108, 100),
    (111, 'Ismael', 'Sciarra', 'ISCIARRA', '515.124.4369', '2005-09-30', 'FI_ACCOUNT', 7700.00, NULL, 108, 100),
    (112, 'Jose Manuel', 'Urman', 'JMURMAN', '515.124.4469', '2006-03-07', 'FI_ACCOUNT', 7800.00, NULL, 108, 100),
    (113, 'Luis', 'Popp', 'LPOPP', '515.124.4567', '2007-12-07', 'FI_ACCOUNT', 6900.00, NULL, 108, 100),
    (114, 'Den', 'Raphaely', 'DRAPHEAL', '515.127.4561', '2002-12-07', 'SA_MAN', 11000.00, 0.30, 100, 80),
    (115, 'Alexander', 'Khoo', 'AKHOO', '515.127.4562', '2003-05-18', 'SA_REP', 9000.00, 0.25, 114, 80),
    (116, 'Shelli', 'Baida', 'SBAIDA', '515.127.4563', '2005-12-24', 'SA_REP', 8600.00, 0.20, 114, 80),
    (145, 'John', 'Russell', 'JRUSSEL', '515.127.4564', '2004-10-01', 'SA_MAN', 14000.00, 0.40, 100, 80),
    (146, 'Karen', 'Partners', 'KPARTNER', '515.127.4565', '2005-01-05', 'SA_REP', 11500.00, 0.30, 145, 80),
    (147, 'Alberto', 'Errazuriz', 'AERRAZUR', '515.127.4566', '2005-03-10', 'SA_REP', 10500.00, 0.30, 145, 80),
    (200, 'Jennifer', 'Whalen', 'JWHALEN', '515.123.4444', '2003-09-17', 'AD_ASST', 4400.00, NULL, 101, 10),
    (201, 'Michael', 'Hartstein', 'MHARTSTE', '515.123.5555', '2004-02-17', 'MK_MAN', 13000.00, NULL, 100, 20),
    (202, 'Pat', 'Fay', 'PFAY', '603.123.6666', '2005-08-17', 'MK_REP', 6000.00, NULL, 201, 20),
    (203, 'Susan', 'Mavris', 'SMAVRIS', '515.123.7777', '2002-06-07', 'HR_REP', 6500.00, NULL, 101, 40);

INSERT INTO job_history (employee_id, start_date, end_date, job_id, department_id) VALUES
    (102, '1998-01-13', '2001-01-12', 'IT_PROG', 60),
    (101, '2001-09-21', '2005-09-20', 'AD_ASST', 10),
    (201, '2002-02-17', '2004-02-16', 'HR_REP', 40),
    (114, '1999-12-07', '2002-12-06', 'SA_REP', 80),
    (200, '2001-09-17', '2003-09-16', 'HR_REP', 40);

COMMIT;
